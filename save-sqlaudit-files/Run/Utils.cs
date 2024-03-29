using System.Text.Json;
using Google.Api;
using Google.Cloud.Audit;
using Google.Cloud.Logging.Type;
using Google.Cloud.Logging.V2;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.SqlServer.XEvent.XELite;
using Parquet;
using Parquet.Data;

namespace AspNetCoreWebApi6
{
    public class Utils
    {
        private static bool CheckFieldStatus(
            string configField,
            string eventField,
            IXEvent xEvent,
            IConfiguration cfg
        )
        {
            bool configFlag = false;

            if ((cfg[configField] != null) && (bool.TryParse(cfg[configField], out configFlag)))
            {
                if (xEvent.Fields.ContainsKey(eventField))
                    return configFlag;

                return false;
            }
            return xEvent.Fields.ContainsKey(eventField);
        }

        public static void WriteMessageToPubSub(
            string projectId,
            string topicId,
            ILogger _logger,
            IXEvent xEvent
        )
        {
            PublisherServiceApiClient publisher = PublisherServiceApiClient.Create();
            TopicName topicName = new TopicName(projectId, topicId);

            PubsubMessage message = new PubsubMessage
            {
                Data = ByteString.CopyFromUtf8(JsonSerializer.Serialize(xEvent)),

                Attributes = { { "Description", "SQL Server Audit message" } }
            };

            publisher.Publish(topicName, new[] { message });
        }

        public static void AddEventToList(
            List<SqlAuditEvent> SQLAuditEventData,
            ILogger _logger,
            IXEvent xEvent
        )
        {
            SqlAuditEvent sqlAuditEvent = new SqlAuditEvent();
            DateTime dateValue;

            if (DateTime.TryParse(xEvent.Fields["event_time"].ToString(), out dateValue))
                sqlAuditEvent.EventTime = dateValue;
            else
                sqlAuditEvent.EventTime = DateTime.MinValue;

            sqlAuditEvent.ClassType = xEvent.Fields["class_type"].ToString();
            sqlAuditEvent.ObjectName = xEvent.Fields["object_name"].ToString();
            sqlAuditEvent.DatabaseName = xEvent.Fields["database_name"].ToString();
            sqlAuditEvent.SessionId = Int32.Parse(xEvent.Fields["session_id"].ToString());
            sqlAuditEvent.Statement = xEvent.Fields["statement"].ToString();
            sqlAuditEvent.SequenceNumber = xEvent.Fields["sequence_number"].ToString();
            sqlAuditEvent.ServerPrincipalName = xEvent.Fields["server_principal_name"].ToString();
            sqlAuditEvent.TransactionId = Int64.Parse(xEvent.Fields["transaction_id"].ToString());
            sqlAuditEvent.ObjectId = Int32.Parse(xEvent.Fields["object_id"].ToString());
            sqlAuditEvent.DatabasePrincipalName = xEvent
                .Fields["database_principal_name"]
                .ToString();
            sqlAuditEvent.ServerInstanceName = xEvent.Fields["server_instance_name"].ToString();
            sqlAuditEvent.ApplicationName = xEvent.Fields["application_name"].ToString();
            sqlAuditEvent.DurationMilliseconds = Int64.Parse(
                xEvent.Fields["duration_milliseconds"].ToString()
            );
            sqlAuditEvent.SchemaName = xEvent.Fields["schema_name"].ToString();
            sqlAuditEvent.Succeeded = Convert.ToBoolean(xEvent.Fields["succeeded"].ToString());
            sqlAuditEvent.ActionId = xEvent.Fields["action_id"].ToString();
            sqlAuditEvent.ConnectionId = xEvent.Fields["connection_id"].ToString();

            SQLAuditEventData.Add(sqlAuditEvent);
        }

        public static void ProcessXEventMessageToEntriesList(
            string projectId,
            string logId,
            IDictionary<string, string> fieldMapping,
            LoggingServiceV2Client loggingServiceV2Client,
            string fileName,
            IXEvent xEvent,
            IConfiguration cfg,
            List<LogEntry> logEntries
        )
        {
            var fileLabel = fileName;
            var logName = new LogName(projectId, logId);

            if (fileName.Contains("_Audit"))
                fileLabel = fileName.Substring(0, fileName.IndexOf("_Audit"));

            LogEntry logEntry = new LogEntry();
            logEntry.LogNameAsLogName = logName;
            logEntry.Severity = LogSeverity.Info;

            var auditLog = new AuditLog();
            auditLog.MethodName = "sqlAudit.custom";

            auditLog.Request = new Struct
            {
                Fields = { ["eventTime"] = Value.ForString(xEvent.Fields["event_time"].ToString()) }
            };

            foreach (KeyValuePair<string, string> entry in fieldMapping)
            {
                if (CheckFieldStatus(entry.Value, entry.Key, xEvent, cfg))
                    auditLog.Request.Fields.Add(
                        entry.Value,
                        Value.ForString(xEvent.Fields[entry.Key].ToString())
                    );
            }

            var payLoad = Any.Pack(auditLog);
            logEntry.ProtoPayload = payLoad;

            logEntries.Add(logEntry);
        }

        public static void WriteMessageToLog(
            string projectId,
            string logId,
            LoggingServiceV2Client loggingServiceV2Client,
            string fileName,
            ILogger _logger,
            List<LogEntry> logEntries
        )
        {
            var fileLabel = fileName;
            var logName = new LogName(projectId, logId);

            if (fileName.Contains("_Audit"))
                fileLabel = fileName.Substring(0, fileName.IndexOf("_Audit"));

            MonitoredResource resource = new MonitoredResource { Type = "global" };

            IDictionary<string, string> entryLabels = new Dictionary<string, string>();
            entryLabels.Add("FileLabel", string.IsNullOrEmpty(fileLabel) ? "" : fileLabel);

            loggingServiceV2Client.WriteLogEntries(logName, resource, entryLabels, logEntries);
            _logger.LogInformation(
                "Saved to Log {count} entries from {fileName}",
                logEntries.Count,
                fileName
            );

            logEntries.Clear();
        }
    }
}
