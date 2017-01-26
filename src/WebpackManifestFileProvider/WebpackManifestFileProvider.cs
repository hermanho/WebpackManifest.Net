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
        private readonly string _webpackManifestFile;
        private readonly PhysicalFilesWatcher _webpackManifestFileWatcher;
        private Dictionary<string, string> _webpackManifestMapping = new Dictionary<string, string>();
        private ILogger<WebpackManifestFileProvider> _logger;

        public WebpackManifestFileProvider(string root, string webpackManifestFile, ILoggerFactory loggerFactory)
        {
            _physicalFileProvider = new PhysicalFileProvider(root);
            _webpackManifestFile = Path.Combine(_physicalFileProvider.Root, webpackManifestFile);
            _logger = loggerFactory.CreateLogger<WebpackManifestFileProvider>();
            _webpackManifestFileWatcher = CreateManifestFileWatcher(root);
            CreateWatchCallback();
            LoadMapping();
        }

        public void Dispose()
        {
            _webpackManifestFileWatcher.Dispose();
            _physicalFileProvider.Dispose();
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
                _logger.LogDebug($"Searching {subpath}");
                subpath = _webpackManifestMapping[subpath];
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
            _logger.LogDebug($"Create Watch Callback");
            var token = _webpackManifestFileWatcher.CreateFileChangeToken(_webpackManifestFile);
            token.RegisterChangeCallback(_ => LoadMapping(), null);
        }

        private void LoadMapping()
        {
            if (File.Exists(_webpackManifestFile))
            {
                try
                {
                    _logger.LogInformation($"Loading JSON {_webpackManifestFile}");
                    using (JsonTextReader reader = new JsonTextReader(File.OpenText(_webpackManifestFile)))
                    {
                        var newMap = new Dictionary<string, string>();
                        JObject jsonObject = (JObject)JToken.ReadFrom(reader);
                        _logger.LogDebug($"JSON read completed");
                        foreach (KeyValuePair<string, JToken> node in jsonObject)
                        {
                            newMap.Add('/' + node.Key, '/' + node.Value.Value<string>());
                        }
                        _webpackManifestMapping = newMap;
                    }
                    _logger.LogInformation($"Loading JSON completed");
                }
                catch (JsonReaderException ex)
                {
                    _logger.LogError(-1, ex, ex.Message);
                }
            }
            else
            {
                _logger.LogDebug($"File does not exist {_webpackManifestFile}");
            }
            CreateWatchCallback();
        }
    }
}
