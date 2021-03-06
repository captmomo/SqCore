using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using SqCommon;
using FinTechCommon;

namespace SqCoreWeb.Controllers
{
    //[Route("WebServer")]
    public class WebServerController : Controller
    {
        private readonly ILogger<Program> m_loggerKestrelStyleDontUse; // Kestrel sends the logs to AspLogger, which will send it back to NLog. It can be used, but practially never use it. Even though this is the official ASP practice. It saves execution resource to not use it. Also, it is more consistent to use Utils.Logger global everywhere in our code.
        private readonly IConfigurationRoot m_configKestrelStyleDontUse; // use the global Utils.Configuration instead. That way you don't have to pass down further in the call stack later
        private readonly IWebAppGlobals m_webAppGlobals;

        public WebServerController(ILogger<Program> p_logger, IConfigurationRoot p_config, IWebAppGlobals p_webAppGlobals)
        {
            m_loggerKestrelStyleDontUse = p_logger;
            m_configKestrelStyleDontUse = p_config;
            m_webAppGlobals = p_webAppGlobals;
        }

        [HttpGet]   // Ping is accessed by the HealthMonitor every 9 minutes (to keep it alive), no no GoogleAuth there
        public ActionResult Ping()
        {
            // pinging Index.html do IO file operation. Also currently it is a Redirection. There must be a quicker way to ping our Webserver. (for keeping it alive)
            // a ping.html or better a c# code that gives back only some bytes, not reading files. E.G. it gives back UTcTime. It has to be quick.
            return Content(@"<HTML><body>Ping. Webserver UtcNow:" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "</body></HTML>", "text/html");
        }

        [HttpGet]
#if !DEBUG
        [Authorize]     // we can live without it, because ControllerCommon.CheckAuthorizedGoogleEmail() will redirect to /login anyway, but it is quicker that this automatically redirects without clicking another URL link.
#endif
        public ActionResult HttpRequestActivityLog()
        {
            HttpRequestLog[] logsPointerArr = new HttpRequestLog[0];
            lock (m_webAppGlobals.HttpRequestLogs)  // prepare for multiple threads
            {
                logsPointerArr = m_webAppGlobals.HttpRequestLogs.ToArray();     // it copies only max 50 pointers to Array. Quick.
            }

            StringBuilder sb = new StringBuilder();
            for (int i = logsPointerArr.Length - 1; i >= 0; i--)        // foreach loop iterates over Queue starting from the oldest item and ending with newest.
            {
                var requestLog = logsPointerArr[i];
                string msg = String.Format("{0}#{1}{2} {3} '{4}' from {5} (u: {6}) ret: {7} in {8:0.00}ms", requestLog.StartTime.ToString("HH':'mm':'ss.f"), requestLog.IsError ? "ERROR in " : String.Empty, requestLog.IsHttps ? "HTTPS" : "HTTP", requestLog.Method, requestLog.Path + (String.IsNullOrEmpty(requestLog.QueryString) ? "" : requestLog.QueryString), requestLog.ClientIP, requestLog.ClientUserEmail, requestLog.StatusCode, requestLog.TotalMilliseconds);
                sb.Append(msg + "<br />");
            }

            return Content(@"<HTML><body><h1>HttpRequests Activity Log</h1><br />" + sb.ToString() + "</body></HTML>", "text/html");
        }

         [HttpGet]
#if !DEBUG
        [Authorize]     // we can live without it, because ControllerCommon.CheckAuthorizedGoogleEmail() will redirect to /login anyway, but it is quicker that this automatically redirects without clicking another URL link.
#endif
        public ActionResult ServerDiagnostics()
        {
            StringBuilder sb = new StringBuilder(@"<HTML><body><h1>ServerDiagnostics</h1>");
            Program.ServerDiagnostic(sb);
            MemDb.gMemDb.ServerDiagnostic(sb);
            DashboardPushHub.ServerDiagnostic(sb);

            return Content(sb.Append("</body></HTML>").ToString(), "text/html");
        }


        [HttpGet]
        public ActionResult HttpRequestHeader()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<html><body>");
            sb.Append("Request.Headers: <br><br>");
            foreach (var header in Request.Headers)
            {
                sb.Append($"{header.Key} : {header.Value} <br>");
            }
            sb.Append("</body></html>");

            return Content(sb.ToString(), "text/html");
        }

        [HttpGet]
        public ActionResult TestHealthMonitorByRaisingExceptionInController()
        {
            var parts = "www.domain.com".Split('.');
            Console.WriteLine(parts[12]);       // raises System.IndexOutOfRangeException()

            StringBuilder sb = new StringBuilder(); // The Code will not arrive here.
            sb.Append("<html><body>");
            sb.Append("TestHealthMonitorEmailByRaisingException: <br><br>");
            sb.Append("</body></html>");

            return Content(sb.ToString(), "text/html");
        }

        [HttpGet]
        public ActionResult TestHealthMonitorByRaisingStrongAssert()
        {
            StrongAssert.Fail(Severity.NoException, "Testing TestHealthMonitorByRaisingStrongAssert() with NoException. ThrowException version of StrongAssert can survive if it is catched.");

            return Content(@"<HTML><body>TestHealthMonitorByRaisingStrongAssert() finished OK. HealthMonitor should have received the message. </body></HTML>", "text/html");
        }

        static void RunUnobservedTaskException()
        {
            Task task1 = new Task(() =>
            {
                throw new ArgumentNullException();
            });

            Task task2 = new Task(() =>
            {
                throw new ArgumentOutOfRangeException();
            });

            task1.Start();
            task2.Start();

            while (!task1.IsCompleted || !task2.IsCompleted)
            {
                Thread.Sleep(50);
            }
        }

        [HttpGet]
        public ActionResult TestHealthMonitorByRaisingUnobservedTaskException()
        {
            Utils.Logger.Info("TestUnobservedTaskException BEGIN");
            // https://stackoverflow.com/questions/3284137/taskscheduler-unobservedtaskexception-event-handler-never-being-triggered

            RunUnobservedTaskException();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            return Content(@"<HTML><body>TestHealthMonitorByRaisingUnobservedTaskException() finished OK. HealthMonitor should have received 2 different exceptions, but it will only send 1 email to admin. <br> Webserver UtcNow:" + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "</body></HTML>", "text/html");
        }

        [HttpGet]
        public ActionResult TestGoogleApiGsheet1()
        {
            Utils.Logger.Info("TestGoogleApiGsheet1() BEGIN");

            string valuesFromGSheetStr = "Error. Make sure GoogleApiKeyKey, GoogleApiKeyKey is in SQLab.WebServer.SQLab.NoGitHub.json !";
            if (!String.IsNullOrEmpty(Utils.Configuration["Google:GoogleApiKeyName"]) && !String.IsNullOrEmpty(Utils.Configuration["Google:GoogleApiKeyKey"]))
            {
                // TODO: not high priority to fix it. It returns code 403, Forbidden. Also the same problem in SqLab. (it might only work on Linux server if IP should have been registered)
                // it works on remote server: https://www.snifferquant.net/WebServer/TestGoogleApiGsheet1   (but not locally, and not in SqLab either)
                // gSheet is public: https://docs.google.com/spreadsheets/d/1onwqrdxQIIUJytd_PMbdFKUXnBx3YSRYok0EmJF8ppM
                if (!Utils.DownloadStringWithRetry(out valuesFromGSheetStr, "https://sheets.googleapis.com/v4/spreadsheets/1onwqrdxQIIUJytd_PMbdFKUXnBx3YSRYok0EmJF8ppM/values/A1%3AA3?key=" + Utils.Configuration["Google:GoogleApiKeyKey"]))
                    valuesFromGSheetStr = "Error in DownloadStringWithRetry().";
            }

            Utils.Logger.Info("TestGoogleApiGsheet1() END");
            return Content($"<HTML><body>TestGoogleApiGsheet1() finished OK. <br> Received data: '{valuesFromGSheetStr}'</body></HTML>", "text/html");
        }

        [HttpGet]
        public ActionResult TestCaretakerCheckFreeDiskSpace()
        {
            Utils.Logger.Info("TestCaretakerCheckFreeDiskSpace() BEGIN");

            StringBuilder noteToClient = new StringBuilder();
            bool success = Caretaker.gCaretaker.CheckFreeDiskSpace(noteToClient);

            Utils.Logger.Info("TestCaretakerCheckFreeDiskSpace() END");
            return Content($"<HTML><body>TestCaretakerCheckFreeDiskSpace() finished with { (success ? "OK" : "Error") }. <br> Note To Client '{noteToClient.ToString()}'</body></HTML>", "text/html");
        }

        [HttpGet]
        public ActionResult TestCaretakerCleanLogfiles()
        {
            Utils.Logger.Info("TestCaretakerCleanLogfiles() BEGIN");

            StringBuilder noteToClient = new StringBuilder();
            bool success = Caretaker.gCaretaker.CleanLogfiles(noteToClient);

            Utils.Logger.Info("TestCaretakerCleanLogfiles() END");
            return Content($"<HTML><body>TestCaretakerCleanLogfiles() finished with { (success ? "OK" : "Error") }. <br> Note To Client '{noteToClient.ToString()}'</body></HTML>", "text/html");
        }

        [HttpPost, HttpGet]     // we only leave HttpGet here so we got a Log message into a log file.
        public ActionResult ReportHealthMonitorCurrentStateToDashboardInJSON()
        {
            return Content(@"<HTML><body>Implement when we bring the HealthMonitorFunctionality from SqLab to SqCore</body></HTML>", "text/html"); // TODO: implement when we bring the HealthMonitorFunctionality from SqLab to SqCore
            // long highResWebRequestReceivedTime = System.Diagnostics.Stopwatch.GetTimestamp();
            // m_logger.LogInformation("ReportHealthMonitorCurrentStateToDashboardInJSON() is called");
            // // TODO: we should check here if it is a HttpGet (or a message without data package) and return gracefully

            // try
            // {
            //     if (Request.Body.CanSeek)
            //     {
            //         Request.Body.Position = 0;                 // Reset the position to zero to read from the beginning.
            //     }
            //     string jsonToBackEnd = new StreamReader(Request.Body).ReadToEnd();

            //     try
            //     {
            //         string receivedTcpMsg = null;
            //         using (var client = new TcpClient())
            //         {
            //             Task task = client.ConnectAsync(ServerIp.HealthMonitorPublicIp, HealthMonitorMessage.DefaultHealthMonitorServerPort);
            //             if (Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))).Result != task)
            //             {
            //                 m_logger.LogError("Error:HealthMonitor server: client.Connect() timeout.");
            //                 return Content(@"{""ResponseToFrontEnd"" : ""Error: Error:HealthMonitor server: client.Connect() timeout.", "application/json");
            //             }

            //             BinaryWriter bw = new BinaryWriter(client.GetStream());
            //             bw.Write((Int32)HealthMonitorMessageID.GetHealthMonitorCurrentStateToHealthMonitorWebsite);
            //             bw.Write(jsonToBackEnd);
            //             bw.Write((Int32)HealthMonitorMessageResponseFormat.JSON);

            //             BinaryReader br = new BinaryReader(client.GetStream());
            //             receivedTcpMsg = br.ReadString();
            //             m_logger.LogDebug("ReportHealthMonitorCurrentStateToDashboardInJSON() returned answer: " + receivedTcpMsg);
            //         }

            //         m_logger.LogDebug("ReportHealthMonitorCurrentStateToDashboardInJSON() after WaitMessageFromWebJob()");
            //         return Content(receivedTcpMsg, "application/json");
            //     }
            //     catch (Exception e)
            //     {
            //         m_logger.LogError("Error:HealthMonitor SendMessage exception:  " + e);
            //         return Content(@"{""ResponseToFrontEnd"" : ""Error:HealthMonitor SendMessage exception. Check log file of the WepApp: " + e.Message, "application/json");
            //     }
            // }
            // catch (Exception ex)
            // {
            //     return Content(@"{""ResponseToFrontEnd"" : ""Error: " + ex.Message + @"""}", "application/json");
            // }


        }
    }
}