using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace WebpackManifestFileProvider
{
    public class WebpackManifestFileProvider : IFileProvider, IDisposable
    {
        private const string PollingEnvironmentKey = "DOTNET_USE_POLLING_FILE_WATCHER";

        private readonly PhysicalFileProvider _physicalFileProvider;
        private readonly string _webpackManifestFileName;
        private readonly string _webpackManifestFileFullPath;
        private readonly PhysicalFilesWatcher _webpackManifestFileWatcher;
        private Dictionary<string, string> _webpackManifestMapping = new Dictionary<string, string>();
        private ILogger<WebpackManifestFileProvider> _logger;
        private IChangeToken _token;

        public WebpackManifestFileProvider(string root, string webpackManifestFile, ILoggerFactory loggerFactory)
        {
            root = EnsureTrailingSlash(root);
            _physicalFileProvider = new PhysicalFileProvider(root);
            _webpackManifestFileName = webpackManifestFile;
            _webpackManifestFileFullPath = Path.Combine(_physicalFileProvider.Root, webpackManifestFile);
            _logger = loggerFactory.CreateLogger<WebpackManifestFileProvider>();
            _webpackManifestFileWatcher = CreateManifestFileWatcher(root);
            LoadMapping();
        }

        public void Dispose()
        {
            _webpackManifestFileWatcher.Dispose();
            _physicalFileProvider.Dispose();
        }

        private static string EnsureTrailingSlash(string path)
        {
            if (!string.IsNullOrEmpty(path) &&
                path[path.Length - 1] != Path.DirectorySeparatorChar)
            {
                return path + Path.DirectorySeparatorChar;
            }

            return path;
        }

        private static PhysicalFilesWatcher CreateManifestFileWatcher(string root)
        {
            var environmentValue = Environment.GetEnvironmentVariable(PollingEnvironmentKey);
            var pollForChanges = string.Equals(environmentValue, "1", StringComparison.Ordinal) ||
                                 string.Equals(environmentValue, "true", StringComparison.OrdinalIgnoreCase);

            return new PhysicalFilesWatcher(root, new FileSystemWatcher(root), pollForChanges);
        }

        public IFileInfo GetFileInfo(string subpath)
        {
            if (_webpackManifestMapping.ContainsKey(subpath))
            {
                string oldsubpath = subpath;
                subpath = _webpackManifestMapping[subpath];
                _logger.LogInformation($"Path {oldsubpath} change to {subpath}");
            }
            return _physicalFileProvider.GetFileInfo(subpath);
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            return _physicalFileProvider.GetDirectoryContents(subpath);
        }

        public IChangeToken Watch(string filter)
        {
            return _physicalFileProvider.Watch(filter);
        }

        private void CreateWatchCallback()
        {
            _logger.LogInformation($"Create Watch Callback");
            if (_token != null)
            {

            }
            _token = _webpackManifestFileWatcher.CreateFileChangeToken(_webpackManifestFileName);
            _token.RegisterChangeCallback(_ =>
            {
                _logger.LogInformation($"JSON file changed {_webpackManifestFileFullPath}");
                LoadMapping();
            }, null);
        }

        private void LoadMapping()
        {
            if (File.Exists(_webpackManifestFileFullPath))
            {
                try
                {
                    System.Threading.Thread.Sleep(200);
                    _logger.LogInformation($"Loading JSON {_webpackManifestFileFullPath}");
                    using (var fs = new FileStream(_webpackManifestFileFullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using (var sr = new StreamReader(fs))
                        {
                            using (JsonTextReader reader = new JsonTextReader(sr))
                            {
                                var newMap = new Dictionary<string, string>();
                                JObject jsonObject = (JObject)JToken.ReadFrom(reader);
                                _logger.LogDebug($"JSON read completed");
                                foreach (KeyValuePair<string, JToken> node in jsonObject)
                                {
                                    newMap.Add('/' + node.Key, '/' + node.Value.Value<string>());
                                }
                                _webpackManifestMapping = newMap;
                                _logger.LogInformation($"Loading JSON completed");
                            }
                        }
                    }
                }
                catch (JsonReaderException ex)
                {
                    _logger.LogError(-1, ex, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(-1, ex, ex.Message);
                    throw;
                }
            }
            else
            {
                _logger.LogDebug($"File does not exist {_webpackManifestFileFullPath}");
            }
            CreateWatchCallback();
        }
    }
}
