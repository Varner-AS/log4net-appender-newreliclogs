using System;
using log4net;
using log4net.Config;

namespace Log4net.Appender.NewRelicLogs.Sample
{
    public class NewRelicLogsSample
    {
        static void Main(string[] args)
        {
            XmlConfigurator.Configure();

            var logger = LogManager.GetLogger(typeof(NewRelicLogsSample));

            logger.Debug("Test debug entry");
            logger.InfoFormat("Test info entry");
            logger.Warn("Test warning entry");
            logger.Error("Test error entry");
            logger.Fatal("Test fatal entry");

            // Throw Exception
            try
            {
                throw new ApplicationException("This is an exception raised to test the New Relic API");
            }
            catch (Exception ex)
            {
                logger.Error("Error with exception", ex);
            }

            ThreadContext.Properties["ContextProperty"] = "TestProperty";
            logger.InfoFormat("Entry with a test property");

            LogManager.Flush(200);
            Console.WriteLine("Press a key to exit.");
            Console.ReadKey(true);
        }
    }
}
