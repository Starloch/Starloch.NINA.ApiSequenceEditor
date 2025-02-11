using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;     // If you do any JObject/JToken manipulation
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Sequencer.Mediator;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Interfaces.Mediator;
using System.Linq;
using System.Collections.Generic;

namespace Starloch.NINA.ApiSequenceEditor
{
    [Export(typeof(IPluginManifest))]
    public class ApiSequenceEditor : PluginBase, INotifyPropertyChanged
    {
        private readonly ISequenceMediator _sequenceMediator;
        private readonly IPluginOptionsAccessor _pluginSettings;

        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private Task _listenerTask;

        private bool _webServerEnabled;
        private int _port;
        private string _serverUrls;

        [ImportingConstructor]
        public ApiSequenceEditor(ISequenceMediator sequenceMediator, IProfileService profileService)
        {
            _sequenceMediator = sequenceMediator;
            _pluginSettings = new PluginOptionsAccessor(profileService, Guid.Parse(this.Identifier));

            // Load settings from plugin options
            LoadSettings();

            RestartServerCommand = new RelayCommand(RestartHttpServer);

            // If user enabled, start immediately
            if (WebServerEnabled)
            {
                StartHttpServer();
            }
        }

        #region Plugin Settings

        private void LoadSettings()
        {
            // For older versions of the plugin system, only GetValueString/SetValueString may exist
            var enabledStr = _pluginSettings.GetValueString(nameof(WebServerEnabled), "false");
            _webServerEnabled = bool.TryParse(enabledStr, out bool en) && en;

            var portStr = _pluginSettings.GetValueString(nameof(Port), "1999");
            if (!int.TryParse(portStr, out _port))
            {
                _port = 1999;
            }

            _serverUrls = $"http://localhost:{_port}/debug";
        }

        private void SaveSettings()
        {
            _pluginSettings.SetValueString(nameof(WebServerEnabled), _webServerEnabled.ToString());
            _pluginSettings.SetValueString(nameof(Port), _port.ToString());
            RaisePropertyChanged(nameof(WebServerEnabled));
            RaisePropertyChanged(nameof(Port));
            RaisePropertyChanged(nameof(ServerUrls));
        }

        /// <summary>
        /// True/False controlling whether the HTTP server should run.
        /// </summary>
        public bool WebServerEnabled
        {
            get => _webServerEnabled;
            set
            {
                if (_webServerEnabled != value)
                {
                    _webServerEnabled = value;
                    if (_webServerEnabled)
                    {
                        StartHttpServer();
                    }
                    else
                    {
                        StopHttpServer();
                    }
                    SaveSettings();
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// Port for the HTTP server.
        /// </summary>
        public int Port
        {
            get => _port;
            set
            {
                if (_port != value)
                {
                    _port = value;
                    RestartHttpServer();
                    SaveSettings();
                    RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// Shows either "Server Stopped" or the URLs if running (or error messages).
        /// </summary>
        public string ServerUrls
        {
            get => _serverUrls;
            private set
            {
                if (_serverUrls != value)
                {
                    _serverUrls = value;
                    RaisePropertyChanged();
                }
            }
        }

        #endregion

        #region HTTP Server Logic

        private void StartHttpServer()
        {
            try
            {
                StopHttpServer(); // Make sure any existing listener is closed

                _listener = new HttpListener();

                // 1) Try to bind to localhost for fewer permission issues
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");

                // If you want a fallback to "http://+:{_port}/" (which requires admin),
                // you can do so in a second try/catch below. But let's start with just localhost:

                _listener.Start();
                _cts = new CancellationTokenSource();
                _listenerTask = Task.Run(() => HandleRequests(_cts.Token));

                // If started successfully, show the debug URL
                ServerUrls = $"http://localhost:{_port}/debug";
            }
            catch (HttpListenerException ex)
            {
                // Optionally fallback to "http://+:{_port}/" if you want to try
                // more general binding. But that often needs admin privs:
                // fallback attempt is commented out here, so you can see the error.

                ServerUrls = $"Failed to start server on port {_port}: {ex.Message}";
            }
        }

        private void StopHttpServer()
        {
            _cts?.Cancel();
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
                _listener = null;
            }
            ServerUrls = "Server Stopped";
        }

        private async Task HandleRequests(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequest(context), token);
                }
                catch
                {
                    if (token.IsCancellationRequested) break;
                }
            }
        }

        private async Task ProcessRequest(HttpListenerContext context)
        {
            try
            {
                string responseString;
                var response = context.Response;
                response.ContentType = "application/json";

                if (context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == "/debug")
                {
                    try
                    {
                        // Retrieve the sequence container
                        var sequenceContainer = GetCurrentSequenceContainer(); // ⬅️ Get the active sequence
                        if (sequenceContainer == null)
                        {
                            responseString = JsonSerializer.Serialize(new { error = "No active sequence found." });
                            response.StatusCode = 404;
                        }
                        else
                        {
                            // Extract `Items` from `SequenceContainer`
                            var itemsProperty = sequenceContainer.GetType().GetProperty("Items");
                            var items = itemsProperty?.GetValue(sequenceContainer) as IEnumerable<object>;

                            // Process items into JSON-friendly format
                            var extractedData = ExtractObjectData(items, 0);

                            var options = new JsonSerializerOptions
                            {
                                WriteIndented = true
                            };

                            responseString = JsonSerializer.Serialize(extractedData, options);
                            response.StatusCode = 200;
                        }
                    }
                    catch (Exception ex)
                    {
                        responseString = JsonSerializer.Serialize(new { error = "Failed to retrieve data", details = ex.Message });
                        response.StatusCode = 500;
                    }
                }
                else
                {
                    response.StatusCode = 404;
                    responseString = JsonSerializer.Serialize(new { error = "Endpoint not found" });
                }

                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                string errorResponse = JsonSerializer.Serialize(new { error = "Internal server error", details = ex.Message });
                byte[] buffer = Encoding.UTF8.GetBytes(errorResponse);
                context.Response.StatusCode = 500;
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            finally
            {
                context.Response.OutputStream.Close();
            }
        }

        private object GetCurrentSequenceContainer()
        {
            try
            {
                // Try to access the sequence root container inside `_sequenceMediator`
                var rootContainerProperty = _sequenceMediator.GetType().GetProperty("AdvancedSequenceRootContainer");

                if (rootContainerProperty != null)
                {
                    return rootContainerProperty.GetValue(_sequenceMediator); // Get the sequence container
                }

                return null;
            }
            catch (Exception ex)
            {
                return new { error = "Could not retrieve Advanced Sequence Root Container", details = ex.Message };
            }
        }

        private const int MAX_DEPTH_LIMIT = 10; // ⬅️ Set this lower if recursion happens

        private object ExtractObjectData(object obj, int depth)
        {
            if (obj == null) return null;
            if (depth > MAX_DEPTH_LIMIT) return new { error = "Max depth reached" };

            var type = obj.GetType();

            // If it's a simple type, return it directly
            if (type.IsPrimitive || obj is string || obj is DateTime)
                return obj.ToString();

            // If it's a collection, process each item
            if (obj is IEnumerable<object> enumerable)
                return enumerable.Select(item => ExtractObjectData(item, depth + 1)).ToList();

            // Process object properties
            var properties = type.GetProperties()
                .Where(p => p.CanRead)
                .ToDictionary(
                    p => p.Name,
                    p => ExtractObjectData(p.GetValue(obj), depth + 1) // ⬅️ Recursively process nested objects
                );

            return new
            {
                Type = type.Name,
                Properties = properties
            };
        }

        #endregion

        #region Commands

        public ICommand RestartServerCommand { get; }

        public void RestartHttpServer()
        {
            StopHttpServer();
            if (WebServerEnabled)
            {
                StartHttpServer();
            }
            SaveSettings();
        }

        #endregion

        #region PluginBase

        public override async Task Teardown()
        {
            StopHttpServer();
            await base.Teardown();
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// Simple relay command for your button binding in Options.xaml
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;

        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute();
    }
}
