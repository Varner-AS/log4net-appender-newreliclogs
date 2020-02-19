# Log4net.Appender.NewRelicLogs
A log4net appender that writes events to version 1 of the [NewRelic Logs](https://docs.newrelic.com/docs/logs/new-relic-logs/get-started/introduction-new-relic-logs) API.

## Summary 
The NewRelic Logs Appender is based on the `BufferingAppenderSkeleton` and bundles `TimeAndLevelEvaluator` allowing to send batches of logs on a periodic bases or when a specific log level occurs.

## Configuration 

Below is a sample configuration, embedded in app.config

```
<configuration>
  <configSections>
    <section name="log4net" type="log4net.Config.Log4NetConfigurationSectionHandler, log4net" />
  </configSections>
  <log4net>
    <appender name="NewRelicLogsAppender" type="Log4net.Appender.NewRelicLogs.NewRelicLogsAppender, Log4net.Appender.NewRelicLogs">
      <bufferSize value="10" />
      <ingestionUrl value="https://log-api.newrelic.com/log/v1" />
      <licenceKey value="#{licenseKey}" />
      <exclueLog4NetProperties value="true" />
      <threshold value="Debug"/>
      <evaluator type="Log4net.Appender.NewRelicLogs.TimeAndLevelEvaluator, Log4net.Appender.NewRelicLogs">
        <threshold value="Error"/>
        <interval value="10" />
      </evaluator>
    </appender>
    <root>
      <level value="Debug" />
      <appender-ref ref="NewRelicLogsAppender" />
    </root>
  </log4net>
  <appSettings>
    <add key="NewRelic.AgentEnabled" value="true" />
    <add key="NewRelic.AppName" value="Log4net.Appender.NewRelicLogs.Sample" />
  </appSettings>
</configuration>
```

## Parameters

The appender uses the value of the "NewRelic.AppName" appSetting to populate at *application* property.

* *ingestionUrl* is the ingestion URL of NewRelic Logs.
* *licenceKey* is the NewRelic License key, which is also used with the NewRelic Agent.
* *insertKey* is New Relic Insert API key. Either *licenseKey* or *insertKey* must be supplied or the appender will enter a disabled state.

The events are submitted to NewRelic Logs in batches, and the appender is derived from `BufferingAppenderSkeleton`. It therefore supports the following parameter:
* *bufferSize* is the maximum number of events to include in a single batch.

To facilitate periodic submission of the buffered log entries, the bundled `TimeAndLevelEvaluator` can be used as outlined in the configuration example. The following parameters are supported:
* *threshold* is one of the valid Levels which would trigger an immediate flush of the logging buffer
* *interval* is the interval in seconds after which a buffer is flushed regardless of the number of events in it. Set to 0 to disable this portion of the evaluator.

The batches are formatted using NewRelic Logs [detailed JSON body](https://docs.newrelic.com/docs/logs/new-relic-logs/log-api/introduction-log-api#json-content) and are transmitted GZip-compressed.

All properties along with the rendered message will be emitted to NewRelic Logs.

Log4net adds several own properties, which may expose GDPR-sensitive data. to avoid logging such data, set `exclueLog4NetProperties` to `true`.

This sink adds the following additional properties:
* *hostname* fetched from `Environment.MachineName`
* *timestamp* in milliseconds since epoch
* *application* holds the value from *NewRelic.AppName* appSetting
* *logger* from the log event's ´LoggerName´ property
* *level* is the actual log level of the event.
* *thread_id* from the log event's ´ThreadName´ property
* *stack_trace* holds the stack trace portion of an exception.

If, during Append, the `newrelic.linkingmetadata` can be retrieved from the NewRelic Agent, it will be unrolled into individual NewRelic properties and used for "logs in context".