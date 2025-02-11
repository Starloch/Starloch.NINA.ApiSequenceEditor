using System;
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
            // Store the bool and int as strings
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
        /// Shows either "Server Stopped" or the URL if running.
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
                // Bind only to localhost for fewer permission issues:
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");

                _listener.Start();
                _cts = new CancellationTokenSource();
                _listenerTask = Task.Run(() => HandleRequests(_cts.Token));

                ServerUrls = $"http://localhost:{_port}/debug";
            }
            catch (HttpListenerException ex)
            {
                ServerUrls = $"Failed to start: {ex.Message}";
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
                string responseString = "";
                var response = context.Response;
                response.ContentType = "application/json";

                if (context.Request.HttpMethod == "GET" && context.Request.Url.AbsolutePath == "/debug")
                {
                    var debugInfo = _sequenceMediator.GetAllTargetsInAdvancedSequence();
                    responseString = JsonSerializer.Serialize(debugInfo, new JsonSerializerOptions { WriteIndented = true });
                    response.StatusCode = 200;
                }
                else
                {
                    response.StatusCode = 404;
                    responseString = JsonSerializer.Serialize(new { error = "Endpoint not found" });
                }

                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch
            {
                context.Response.StatusCode = 500;
            }
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

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute();
    }
}
