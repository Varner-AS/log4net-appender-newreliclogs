using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using log4net.Appender;
using log4net.Core;
using log4net.Util;
using NewRelic.Api.Agent;
using Newtonsoft.Json;
using static System.String;

namespace Log4net.Appender.NewRelicLogs
{
    public class NewRelicLogsAppender : BufferingAppenderSkeleton
    {
        public string IngestionUrl { get; set; }
        public string LicenceKey { get; set; }
        public string InsertKey { get; set; }
        public bool ExclueLog4NetProperties { get; set; }

        private string AppName { get; }

        private const string LinkingMetadataKey = "newrelic.linkingmetadata";
        private IAgent Agent { get; } = NewRelic.Api.Agent.NewRelic.GetAgent();

        public NewRelicLogsAppender()
        {
            AppName = ConfigurationManager.AppSettings.Get("NewRelic.AppName");
        }

        private bool LoggerDisabled
        {
            get
            {
                var licenceKeySet = !IsNullOrWhiteSpace(LicenceKey) && !LicenceKey.StartsWith("#{");
                var insertKeySet = !IsNullOrWhiteSpace(InsertKey) && !InsertKey.StartsWith("#{");
                return !(licenceKeySet || insertKeySet)
                       || IsNullOrWhiteSpace(IngestionUrl)
                       || IsNullOrWhiteSpace(AppName);
            }
        }

        protected override bool PreAppendCheck()
        {
            return !LoggerDisabled && base.PreAppendCheck();
        }

        /// <summary>
        /// Reimplementation from:
        /// https://github.com/newrelic/newrelic-logenricher-dotnet/blob/master/src/Log4Net/NewRelic.LogEnrichers.Log4Net/NewRelicAppender.cs
        /// </summary>
        /// <param name="loggingEvent"></param>
        protected override void Append(LoggingEvent loggingEvent)
        {
            try
            {
                var linkingMetadata = Agent.GetLinkingMetadata();

                if (linkingMetadata != null && linkingMetadata.Keys.Count != 0)
                {
                    loggingEvent.Properties[LinkingMetadataKey] = linkingMetadata;
                }
            }
            catch (Exception ex)
            {
                LogLog.Error(GetType(), "Failed to append NewRelic logging metadata", ex);
            }

            base.Append(loggingEvent);
        }

        protected override void SendBuffer(LoggingEvent[] events)
        {
            if (LoggerDisabled) return;

            dynamic detailedLogObject = new
            {
                common = new
                {
                    attributes = new Dictionary<string, string>
                    {
                        { "hostname", Environment.MachineName },
                        { "application", AppName }
                    }
                },

                logs = new List<object>()
            };

            foreach (var logEvent in events)
            {
                try
                {
                    dynamic logEntry = new
                    {
                        timestamp = UnixTimestampFromDateTime(logEvent.TimeStampUtc),
                        message = logEvent.RenderedMessage,
                        attributes = new Dictionary<string, string>
                        {
                            { "level", logEvent.Level.ToString() },
                            { "logger", logEvent.LoggerName },
                            { "thread_id", logEvent.ThreadName },
                            { "stack_trace", logEvent.ExceptionObject?.StackTrace ?? "" }
                        }
                    };

                    foreach (var propKey in logEvent.Properties.GetKeys())
                    {
                        if (propKey.Equals(LinkingMetadataKey, StringComparison.InvariantCultureIgnoreCase))
                        {
                            continue;
                        }

                        if (ExclueLog4NetProperties && propKey.StartsWith("log4net", StringComparison.CurrentCultureIgnoreCase))
                        {
                            continue;
                        }

                        logEntry.attributes.Add(propKey, logEvent.Properties[propKey] as string ?? "");
                    }

                    UnrollNewRelicDistributedTraceAttributes(logEntry, logEvent);

                    detailedLogObject.logs.Add(logEntry);
                }
                catch (Exception e)
                {
                    LogLog.Error(GetType(), "Self-log: Failed to format event data", e);
                }
            }

            var body = Serialize(new List<object> { detailedLogObject });

            ThreadPool.QueueUserWorkItem(task =>
            {
                // Protect against Thread.Abort
                try { }
                finally
                {
                    try
                    {
                        var startTime = DateTime.Now;
                        SendToNewRelic(body);

                        if (LogLog.IsDebugEnabled)
                        {
                            LogLog.Debug(GetType(),
                                         $"Self-log: Used {(DateTime.Now - startTime).TotalMilliseconds} milliseconds to send {body.Length} bytes in {events.Length} log events to NewRelic");
                        }
                    }
                    catch (Exception e)
                    {
                        LogLog.Error(GetType(), "Self-log: Failed to send data to NewRelic Logs", e);
                    }
                }
            });
        }

        private void SendToNewRelic(string body)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            if (!(WebRequest.Create(IngestionUrl) is HttpWebRequest request))
            {
                return;
            }

            if (!IsNullOrWhiteSpace(LicenceKey))
            {
                request.Headers.Add("X-License-Key", LicenceKey);
            }
            else
            {
                request.Headers.Add("X-Insert-Key", InsertKey);
            }

            request.Headers.Add("Content-Encoding", "gzip");

            request.Timeout = 40000; //It's basically fire-and-forget
            request.Credentials = CredentialCache.DefaultCredentials;
            request.ContentType = "application/gzip";
            request.Accept = "*/*";
            request.Method = "POST";
            request.KeepAlive = false;

            var byteStream = Encoding.UTF8.GetBytes(body);

            try
            {
                using (var zippedRequestStream = new GZipStream(request.GetRequestStream(), CompressionMode.Compress))
                {
                    zippedRequestStream.Write(byteStream, 0, byteStream.Length);
                    zippedRequestStream.Flush();
                    zippedRequestStream.Close();
                }
            }
            catch (WebException e)
            {
                LogLog.Error(GetType(), "Self-log: Failed to create WebRequest to NewRelic Logs", e);
                return;
            }

            try
            {
                using (var response = request.GetResponse() as HttpWebResponse)
                {
                    if (response == null || response.StatusCode != HttpStatusCode.Accepted)
                    {
                        LogLog.Error(GetType(), $"Self-log: Response from NewRelic Logs is missing or negative: {response?.StatusCode}");
                    }
                }
            }
            catch (WebException e)
            {
                LogLog.Error(GetType(), "Self-log: Failed to parse response from NewRelic Logs", e);
            }
        }

        private string Serialize(object obj)
        {
            var serializer = new JsonSerializer();

            //Stipulate 350 bytes per log entry on average
            var json = new StringBuilder(BufferSize * 350);

            using (var sw = new StringWriter(json))
            {
                using (var jw = new JsonTextWriter(sw))
                {
                    serializer.Serialize(jw, obj);
                }
            }

            return json.ToString();
        }

        /// <summary>
        /// Converts from DateTime to Unix time. Conversion is timezone-agnostic.
        /// </summary>
        /// <param name="date"></param>
        private static long UnixTimestampFromDateTime(DateTime date)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            if (date == DateTime.MinValue) return 0;

            return (long) (date - epoch).TotalMilliseconds;
        }

        private static void UnrollNewRelicDistributedTraceAttributes(dynamic logEntry, LoggingEvent logEvent)
        {
            if (!logEvent.Properties.Contains(LinkingMetadataKey))
            {
                return;
            }

            if (logEvent.Properties[LinkingMetadataKey] is Dictionary<string, string> newRelicMetaData)
            {
                foreach (var newRelicProperty in newRelicMetaData)
                {
                    logEntry.attributes.Add(newRelicProperty.Key, newRelicProperty.Value);
                }
            }
        }
    }
}
