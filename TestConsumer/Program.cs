// Copyright 2016-2018 Confluent Inc., 2015-2016 Andreas Heider
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Derived from: rdkafka-dotnet, licensed under the 2-clause BSD License.
//
// Refer to LICENSE for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestConsumer;

/// <summary>
///     Demonstrates use of the Consumer client.
/// </summary>
namespace Confluent.Kafka.Examples.ConsumerExample
{
    public class Program
    {
        public static void MyErrorHandler<T,U>(IConsumer<T, U> consumer, Error error)
        {
            Console.WriteLine($"Error: {consumer.Name} {error.Reason}");
        }

        public static void MyLogHandler<T, U>(IConsumer<T, U> consumer, LogMessage logMessage)
        {
            Console.WriteLine($"Log: {consumer.Name} {logMessage.Message}");
        }

        public static void ReadDatas(string brokerList, string topic, int partition, int offsetMin, int offsetMax)
        {
            try
            {
                Console.WriteLine("Start read datas");
                var config = new ConsumerConfig
                {
                    BootstrapServers = brokerList,
                    GroupId = "csharp-consumer",
                    EnableAutoCommit = false,
                    SessionTimeoutMs = 6000,
                   // AutoOffsetReset = AutoOffsetReset.Earliest,                    
                    EnablePartitionEof = true
                };
                var consumerBuilder = new ConsumerBuilder<string, string>(config);
                consumerBuilder.SetErrorHandler(MyErrorHandler);
                consumerBuilder.SetLogHandler(MyLogHandler);

                var consumer = consumerBuilder.Build();

                // Read datas from
                consumer.Assign(new TopicPartitionOffset(topic, new Partition(partition), new Offset(offsetMin)));
                ConsumeResult<string, string> result;
                var sw = new Stopwatch();
                do
                {
                    sw.Restart();
                    result = consumer.Consume(10_000);
                    Console.WriteLine(sw.ElapsedMilliseconds);
                    if (result != null)
                    {
                        Console.WriteLine($"{result.IsPartitionEOF} {result.TopicPartitionOffset}: '{result.Message?.Key}' | '{result.Message?.Value}'");              
                    }
                }
                while (result != null && !result.IsPartitionEOF && result.Offset < offsetMax);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }







        /// <summary>
        ///     In this example
        ///         - offsets are manually committed.
        ///         - no extra thread is created for the Poll (Consume) loop.
        /// </summary>
        public static void Run_Consume(string brokerList, List<string> topics, CancellationToken cancellationToken)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = brokerList,
                GroupId = "csharp-consumer",
                EnableAutoCommit = false,
                StatisticsIntervalMs = 0, // Emit statistics message every 5s
                SessionTimeoutMs = 6000,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnablePartitionEof = true
            };

            const int commitPeriod = 5;

            // Note: If a key or value deserializer is not set (as is the case below), the 
            // deserializer corresponding to the appropriate type from Confluent.Kafka.Deserializers
            // will be used automatically (where available). The default deserializer for string
            // is UTF8. The default deserializer for Ignore returns null for all input data
            // (including non-null data).
            using (var consumer = new ConsumerBuilder<Ignore, string>(config)
                // Note: All handlers are called on the main .Consume thread.
                .SetErrorHandler((_, e) => Console.WriteLine($"Error: {e.Reason}"))
                .SetStatisticsHandler((_, json) => Console.WriteLine($"Statistics: {json}"))
                .SetPartitionsAssignedHandler((c, partitions) =>
                {
                    Console.WriteLine($"Assigned partitions: [{string.Join(", ", partitions)}]");
                    // possibly manually specify start offsets or override the partition assignment provided by
                    // the consumer group by returning a list of topic/partition/offsets to assign to, e.g.:
                    // 
                    // return partitions.Select(tp => new TopicPartitionOffset(tp, externalOffsets[tp]));
                })
                .SetPartitionsRevokedHandler((c, partitions) =>
                {
                    Console.WriteLine($"Revoking assignment: [{string.Join(", ", partitions)}]");
                })
                .Build())
            {
                // Read datas from
                //consumer.Seek(new TopicPartitionOffset("toto", new Partition(10), new Offset(123)));
                //var result = consumer.Consume(100);
                //Console.WriteLine(result.TopicPartitionOffset);
                

                consumer.Subscribe(topics);

                try
                {
                    while (true)
                    {
                        try
                        {
                            var consumeResult = consumer.Consume(cancellationToken);

                            if (consumeResult.IsPartitionEOF)
                            {
                                Console.WriteLine(
                                    $"Reached end of topic {consumeResult.Topic}, partition {consumeResult.Partition}, offset {consumeResult.Offset}.");

                                continue;
                            }

                            Console.WriteLine($"Received message at {consumeResult.TopicPartitionOffset}: {consumeResult.Message.Value}");

                            if (consumeResult.Offset % commitPeriod == 0)
                            {
                                // The Commit method sends a "commit offsets" request to the Kafka
                                // cluster and synchronously waits for the response. This is very
                                // slow compared to the rate at which the consumer is capable of
                                // consuming messages. A high performance application will typically
                                // commit offsets relatively infrequently and be designed handle
                                // duplicate messages in the event of failure.
                                try
                                {
                                    consumer.Commit(consumeResult);
                                }
                                catch (KafkaException e)
                                {
                                    Console.WriteLine($"Commit error: {e.Error.Reason}");
                                }
                            }
                        }
                        catch (ConsumeException e)
                        {
                            Console.WriteLine($"Consume error: {e.Error.Reason}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Closing consumer.");
                    consumer.Close();
                }
            }
        }

        /// <summary>
        ///     In this example
        ///         - consumer group functionality (i.e. .Subscribe + offset commits) is not used.
        ///         - the consumer is manually assigned to a partition and always starts consumption
        ///           from a specific offset (0).
        /// </summary>
        public static void Run_ManualAssign(string brokerList, List<string> topics, CancellationToken cancellationToken)
        {
            var config = new ConsumerConfig
            {
                // the group.id property must be specified when creating a consumer, even 
                // if you do not intend to use any consumer group functionality.
                GroupId = new Guid().ToString(),
                BootstrapServers = brokerList,
                // partition offsets can be committed to a group even by consumers not
                // subscribed to the group. in this example, auto commit is disabled
                // to prevent this from occurring.
                EnableAutoCommit = true
            };

            using (var consumer =
                new ConsumerBuilder<Ignore, string>(config)
                    .SetErrorHandler((_, e) => Console.WriteLine($"Error: {e.Reason}"))
                    .Build())
            {
                consumer.Assign(topics.Select(topic => new TopicPartitionOffset(topic, 0, Offset.Beginning)).ToList());

                try
                {
                    while (true)
                    {
                        try
                        {
                            var consumeResult = consumer.Consume(cancellationToken);
                            // Note: End of partition notification has not been enabled, so
                            // it is guaranteed that the ConsumeResult instance corresponds
                            // to a Message, and not a PartitionEOF event.
                            Console.WriteLine($"Received message at {consumeResult.TopicPartitionOffset}: ${consumeResult.Message.Value}");
                        }
                        catch (ConsumeException e)
                        {
                            Console.WriteLine($"Consume error: {e.Error.Reason}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Closing consumer.");
                    consumer.Close();
                }
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: .. <subscribe|manual> <broker,broker,..> <topic> [topic..]");
            Console.WriteLine("Ex:  Ubuntu-x64:9092 Toto / TestPartition");
        }

        public static void DoSomeTests()
        {
            Console.WriteLine("Start some tests...");
            try
            {
                SomeTests.Test1();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            Console.WriteLine("End some tests...");
        }

        public static void Main(string[] args)
        {
             //DoSomeTests(); return; 
            
            if (args.Length < 3)
            {
                PrintUsage();
                return;
            }

            var mode = args[0];
            var brokerList = args[1];
            var topics = args.Skip(2).ToList();

            Console.WriteLine($"Started consumer, Ctrl-C to stop consuming");

            CancellationTokenSource cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => {
                e.Cancel = true; // prevent the process from terminating.
                cts.Cancel();
            };

            ReadDatas(brokerList, topics[0], 2, 5, 100);
            return;

            switch (mode)
            {
                case "subscribe":
                    Run_Consume(brokerList, topics, cts.Token);
                    break;
                case "manual":
                    Run_ManualAssign(brokerList, topics, cts.Token);
                    break;
                default:
                    PrintUsage();
                    break;
            }
        }
    }
}
