using Confluent.Kafka;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestConsumer
{
    public static class SomeTests
    {
        public static void Test1()
        {
            var dico = new Dictionary<string, string>();
            // dico.Add("toto", "toto_value"); // throw if invalid param
            dico.Add("session.timeout.ms", "100");
            var config = new ConsumerConfig(dico);
            config.SessionTimeoutMs = 1000; // Modify dico value


            config.GroupId = new Guid().ToString(); // Mandatory            
            using (var consumer =
               new ConsumerBuilder<Ignore, string>(config)
                   .SetErrorHandler((_, e) => Console.WriteLine($"Error: {e.Reason}"))
                   .Build())
            {

            }
        }
    }
}
