// https://github.com/AnderssonPeter/CompressedStaticFiles
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SqCoreWeb
{
    public class CompressedStaticFileMiddleware
    {
        private static Dictionary<string, string> compressionTypes =
            new Dictionary<string, string>()
            {
                { "gzip", ".gz" }, {"br", ".br" }
            };

        private readonly IOptions<StaticFileOptions> _staticFileOptions;
        private readonly StaticFileMiddleware _base;
        private readonly ILogger _logger;

        public CompressedStaticFileMiddleware(RequestDelegate next, IWebHostEnvironment hostingEnv, IOptions<StaticFileOptions> staticFileOptions, ILoggerFactory loggerFactory)
        {
            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            if (hostingEnv == null)
            {
                throw new ArgumentNullException(nameof(hostingEnv));
            }

            _logger = loggerFactory.CreateLogger<CompressedStaticFileMiddleware>();


            this._staticFileOptions = staticFileOptions ?? throw new ArgumentNullException(nameof(staticFileOptions));
            InitializeStaticFileOptions(hostingEnv, staticFileOptions);

            _base = new StaticFileMiddleware(next, hostingEnv, staticFileOptions, loggerFactory);
        }

        private static void InitializeStaticFileOptions(IWebHostEnvironment hostingEnv, IOptions<StaticFileOptions> staticFileOptions)
        {
            staticFileOptions.Value.FileProvider = staticFileOptions.Value.FileProvider ?? hostingEnv.WebRootFileProvider;
            var contentTypeProvider = staticFileOptions.Value.ContentTypeProvider ?? new FileExtensionContentTypeProvider();
            if (contentTypeProvider is FileExtensionContentTypeProvider fileExtensionContentTypeProvider)
            {
                // the StaticFileProvider would not serve the file if it does not know the content-type
                fileExtensionContentTypeProvider.Mappings[".br"] = "application/brotli";
            }
            staticFileOptions.Value.ContentTypeProvider = contentTypeProvider;

            var originalPrepareResponse = staticFileOptions.Value.OnPrepareResponse;
            // Called after the status code and headers have been set, but before the body has been written. This can be used to add or change the response headers.
            staticFileOptions.Value.OnPrepareResponse = ctx =>
            {
                originalPrepareResponse(ctx);   // we let StaticFileMiddleware create finalize the header, then we rewrite it.
                foreach (var compressionType in compressionTypes.Keys)
                {
                    var fileExtension = compressionTypes[compressionType];
                    if (ctx.File.Name.EndsWith(fileExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        // we need to restore the original content type, otherwise it would be based on the compression type
                        // (for example "application/brotli" instead of "text/html")
                        string contentType = String.Empty;
                        if (contentTypeProvider.TryGetContentType(ctx.File.PhysicalPath.Remove(
                            ctx.File.PhysicalPath.Length - fileExtension.Length, fileExtension.Length), out contentType))
                            ctx.Context.Response.ContentType = contentType;
                        ctx.Context.Response.Headers.Add("Content-Encoding", new[] { compressionType });
                    }
                }
            };
        }

        public Task Invoke(HttpContext context)
        {
            if (context.Request.Path.HasValue)
            {
                ProcessRequest(context);
            }
            return _base.Invoke(context);   // first, we preprocess the request (change context.Request.Path), and then we let StaticFileMiddleware load it from disk
        }

        private void ProcessRequest(HttpContext context)
        {
            var fileSystem = _staticFileOptions.Value.FileProvider;
            var originalFile = fileSystem.GetFileInfo(context.Request.Path);

            if (!originalFile.Exists)
            {
                return;
            }

            var supportedEncodings = GetSupportedEncodings(context);

            // try to find a compressed version of the file and ensure that it is smaller than the uncompressed version
            IFileInfo matchedFile = originalFile;
            foreach (var compressionType in supportedEncodings)
            {
                var fileExtension = compressionTypes[compressionType];
                var file = fileSystem.GetFileInfo(context.Request.Path + fileExtension);
                if (file.Exists && file.Length < matchedFile.Length)
                {
                    matchedFile = file;
                }
            }

            if (matchedFile != originalFile)
            {
                // a compressed version exists and is smaller, change the path to serve the compressed file
                var matchedPath = context.Request.Path.Value + Path.GetExtension(matchedFile.Name);
                
                //_logger.LogFileServed(context.Request.Path.Value, matchedPath, originalFile.Length, matchedFile.Length);
                string logStr = $"Sending file. Request file: '{context.Request.Path.Value}'. Served file: '{matchedPath}'. Original file size: {originalFile.Length}. Served file size: {matchedFile.Length}";
                Console.WriteLine(logStr);
                
                context.Request.Path = new PathString(matchedPath);
            }
        }

        /// <summary>
        /// Find the encodings that are supported by the browser and by this middleware
        /// </summary>
        private static IEnumerable<string> GetSupportedEncodings(HttpContext context)
        {
            var browserSupportedCompressionTypes = context.Request.Headers["Accept-Encoding"].ToString().Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var validCompressionTypes = compressionTypes.Keys.Intersect(browserSupportedCompressionTypes, StringComparer.OrdinalIgnoreCase);
            return validCompressionTypes;
        }
    }


    public static class CompressedStaticFileExtensions
    {
        public static IApplicationBuilder UseCompressedStaticFiles(this IApplicationBuilder app)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            return app.UseMiddleware<CompressedStaticFileMiddleware>();
        }


        public static IApplicationBuilder UseCompressedStaticFiles(this IApplicationBuilder app, StaticFileOptions options)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            return app.UseMiddleware<CompressedStaticFileMiddleware>(Options.Create(options));
        }
    }
}