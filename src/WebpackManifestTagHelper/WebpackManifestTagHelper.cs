using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Razor.TagHelpers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WebpackManifest.Net
{
    [HtmlTargetElement("link", Attributes = ManifestAttributeName, TagStructure = TagStructure.WithoutEndTag)]
    [HtmlTargetElement("script", Attributes = ManifestAttributeName, TagStructure = TagStructure.WithoutEndTag)]
    [HtmlTargetElement("img", Attributes = ManifestAttributeName, TagStructure = TagStructure.WithoutEndTag)]
    public class WebpackManifestTagHelper : UrlResolutionTagHelper
    {
        private const string ManifestAttributeName = "asp-webpack-manifest";

        //https://github.com/aspnet/Mvc/blob/rel/1.1.0/src/Microsoft.AspNetCore.Mvc.Razor/TagHelpers/UrlResolutionTagHelper.cs
        private static readonly Dictionary<string, string[]> ElementAttributeLookups =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "a", new[] { "href" } },
                { "applet", new[] { "archive" } },
                { "area", new[] { "href" } },
                { "audio", new[] { "src" } },
                { "base", new[] { "href" } },
                { "blockquote", new[] { "cite" } },
                { "button", new[] { "formaction" } },
                { "del", new[] { "cite" } },
                { "embed", new[] { "src" } },
                { "form", new[] { "action" } },
                { "html", new[] { "manifest" } },
                { "iframe", new[] { "src" } },
                { "img", new[] { "src", "srcset" } },
                { "input", new[] { "src", "formaction" } },
                { "ins", new[] { "cite" } },
                { "link", new[] { "href" } },
                { "menuitem", new[] { "icon" } },
                { "object", new[] { "archive", "data" } },
                { "q", new[] { "cite" } },
                { "script", new[] { "src" } },
                { "source", new[] { "src", "srcset" } },
                { "track", new[] { "src" } },
                { "video", new[] { "poster", "src" } },
            };

        private ILogger<WebpackManifestTagHelper> _logger;
        private string _webpackManifestPath;
        private Dictionary<string, string> _webpackManifestMapping = new Dictionary<string, string>();

        public WebpackManifestTagHelper(IHostingEnvironment env,
            ILogger<WebpackManifestTagHelper> logger,
            IUrlHelperFactory urlHelperFactory,
            HtmlEncoder htmlEncoder) : base(urlHelperFactory, htmlEncoder)
        {
            _logger = logger;
            _webpackManifestPath = Path.Combine(env.WebRootPath, "manifest.json");
            _logger.LogInformation($"Loading file {_webpackManifestPath}");
            LoadMapping();
        }

        [HtmlAttributeName(ManifestAttributeName)]
        public bool? ManifestLoopup { get; set; }

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            if (output.TagName == null)
            {
                return;
            }

            if (File.Exists(_webpackManifestPath) || File.Exists(_webpackManifestPath) && ManifestLoopup == true)
            {
                string[] attributeNames;
                if (ElementAttributeLookups.TryGetValue(output.TagName, out attributeNames))
                {
                    for (var i = 0; i < attributeNames.Length; i++)
                    {
                        ProcessUrlAttribute(attributeNames[i], output);
                    }
                }
            }
        }

        protected new void ProcessUrlAttribute(string attributeName, TagHelperOutput output)
        {
            if (attributeName == null)
            {
                throw new ArgumentNullException(nameof(attributeName));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            for (var i = 0; i < output.Attributes.Count; i++)
            {
                var attribute = output.Attributes[i];
                if (!string.Equals(attribute.Name, attributeName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var stringValue = attribute.Value as string;
                if (stringValue != null)
                {
                    if (_webpackManifestMapping.ContainsKey(stringValue))
                    {
                        _logger.LogInformation($"Replacing {attribute.Name}:{stringValue} to {_webpackManifestMapping[stringValue]} (stringValue)");
                        output.Attributes[i] = new TagHelperAttribute(
                            attribute.Name,
                            _webpackManifestMapping[stringValue],
                            attribute.ValueStyle);
                    }
                    else
                    {
                        _logger.LogInformation($"Cannot find the mapping for {attribute.Name}:{stringValue} (stringValue)");
                    }
                }
                else
                {
                    var htmlContent = attribute.Value as IHtmlContent;
                    if (htmlContent != null)
                    {
                        var htmlString = htmlContent as HtmlString;
                        if (htmlString != null)
                        {
                            // No need for a StringWriter in this case.
                            stringValue = htmlString.ToString();
                        }
                        else
                        {
                            using (var writer = new StringWriter())
                            {
                                htmlContent.WriteTo(writer, HtmlEncoder);
                                stringValue = writer.ToString();
                            }
                        }

                        if (_webpackManifestMapping.ContainsKey(stringValue))
                        {
                            _logger.LogInformation($"Replacing {attribute.Name}:{stringValue} to {_webpackManifestMapping[stringValue]} (IHtmlContent)");
                            output.Attributes[i] = new TagHelperAttribute(
                                attribute.Name,
                                _webpackManifestMapping[stringValue],
                                attribute.ValueStyle);
                        }
                        else
                        {
                            _logger.LogInformation($"Cannot find the mapping for {attribute.Name}:{stringValue} (IHtmlContent)");
                            // Not a ~/ URL. Just avoid re-encoding the attribute value later.
                            output.Attributes[i] = new TagHelperAttribute(
                                attribute.Name,
                                new HtmlString(stringValue),
                                attribute.ValueStyle);
                        }
                    }
                }
            }
        }

        private void LoadMapping()
        {
            if (File.Exists(_webpackManifestPath))
            {
                try
                {
                    System.Threading.Thread.Sleep(200);
                    _logger.LogInformation($"Loading JSON {_webpackManifestPath}");
                    using (var fs = new FileStream(_webpackManifestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using (var sr = new StreamReader(fs))
                        {
                            using (JsonTextReader reader = new JsonTextReader(sr))
                            {
                                var newMap = new Dictionary<string, string>();
                                JObject jsonObject = (JObject)JToken.ReadFrom(reader);
                                _logger.LogInformation($"JSON read completed");
                                foreach (KeyValuePair<string, JToken> node in jsonObject)
                                {
                                    newMap.Add('/' + node.Key, '/' + node.Value.Value<string>());
                                }
                                _webpackManifestMapping = newMap;
                            }
                            _logger.LogInformation($"JSON LoadMapping completed");
                        }
                    }
                }
                catch (JsonReaderException ex)
                {
                    _logger.LogError(-1, ex, ex.Message);
                    throw new Exception($"Invalid JSON file {_webpackManifestPath}. Please run webpack", ex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(-1, ex, ex.Message);
                    throw;
                }
            }
            else
            {
                _logger.LogDebug($"File does not exist {_webpackManifestPath}");
            }
        }
    }
}
