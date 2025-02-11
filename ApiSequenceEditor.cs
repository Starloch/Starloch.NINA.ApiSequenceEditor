using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Mediator;
using NINA.Sequencer.Container; // Provides SequenceRootContainer & ISequenceRootContainer
using NINA.Sequencer;
using NINA.Astrometry;
using NINA.Sequencer.Interfaces.Mediator;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Starloch.NINA.ApiSequenceEditor
{
    [Export(typeof(IPluginManifest))]
    public class ApiSequenceEditor : PluginBase, INotifyPropertyChanged
    {
        private readonly ISequenceMediator _sequenceMediator;
        private readonly IPluginOptionsAccessor _pluginSettings;
        private readonly IProfileService _profileService;

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
            _profileService = profileService;
            _pluginSettings = new PluginOptionsAccessor(profileService, Guid.Parse(this.Identifier));

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
            var enabledStr = _pluginSettings.GetValueString(nameof(WebServerEnabled), "false");
            _webServerEnabled = bool.TryParse(enabledStr, out bool en) && en;

            var portStr = _pluginSettings.GetValueString(nameof(Port), "1999");
            _port = int.TryParse(portStr, out int p) ? p : 1999;

            ServerUrls = $"http://localhost:{_port}/debug";
        }

        private void SaveSettings()
        {
            _pluginSettings.SetValueString(nameof(WebServerEnabled), _webServerEnabled.ToString());
            _pluginSettings.SetValueString(nameof(Port), _port.ToString());
            RaisePropertyChanged(nameof(WebServerEnabled));
            RaisePropertyChanged(nameof(Port));
            RaisePropertyChanged(nameof(ServerUrls));
        }

        public bool WebServerEnabled
        {
            get => _webServerEnabled;
            set
            {
                if (_webServerEnabled != value)
                {
                    _webServerEnabled = value;
                    if (_webServerEnabled)
                        StartHttpServer();
                    else
                        StopHttpServer();
                    SaveSettings();
                    RaisePropertyChanged();
                }
            }
        }

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

        public ICommand RestartServerCommand { get; }

        public void RestartHttpServer()
        {
            StopHttpServer();
            if (WebServerEnabled)
                StartHttpServer();
            SaveSettings();
        }

        #endregion

        #region HTTP Server Logic

        private void StartHttpServer()
        {
            try
            {
                StopHttpServer();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Start();
                _cts = new CancellationTokenSource();
                _listenerTask = Task.Run(() => HandleRequests(_cts.Token));
                ServerUrls = $"PUT /sequence/load?sequencename=xxx OR send JSON in payload to update sequence.";
            }
            catch (HttpListenerException ex)
            {
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
                    if (token.IsCancellationRequested)
                        break;
                }
            }
        }

        private async Task ProcessRequest(HttpListenerContext context)
        {
            string responseString = "";
            int statusCode = 200;
            try
            {
                string path = context.Request.Url.AbsolutePath.ToLower();
                string method = context.Request.HttpMethod.ToUpperInvariant();
                if (method == "PUT" && path == "/sequence/load")
                {
                    // Load sequence from file if a "sequencename" query parameter is provided; otherwise from payload.
                    string seqName = context.Request.QueryString["sequencename"];
                    SequenceRootContainer container = null;
                    if (!string.IsNullOrWhiteSpace(seqName))
                    {
                        IProfile profile = _profileService.ActiveProfile;
                        string folder = profile.SequenceSettings.DefaultSequenceFolder;
                        string filePath = Path.Combine(folder, seqName + ".json");
                        if (!File.Exists(filePath))
                        {
                            statusCode = 404;
                            responseString = JsonConvert.SerializeObject(new { error = "Sequence file not found." });
                        }
                        else
                        {
                            string json = File.ReadAllText(filePath);
                            try
                            {
                                var settings = new JsonSerializerSettings
                                {
                                    TypeNameHandling = TypeNameHandling.Auto,
                                    SerializationBinder = new SimpleBinder(),
                                    TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                                    MissingMemberHandling = MissingMemberHandling.Ignore,
                                    PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                                    NullValueHandling = NullValueHandling.Include
                                };
                                container = JsonConvert.DeserializeObject<SequenceRootContainer>(json, settings);
                            }
                            catch (Exception ex)
                            {
                                string inner = ex.InnerException != null ? ex.InnerException.Message : "";
                                statusCode = 500;
                                responseString = JsonConvert.SerializeObject(new { error = "Error deserializing sequence from file.", details = ex.Message, innerException = inner });
                            }
                        }
                    }
                    else
                    {
                        using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                        string json = await reader.ReadToEndAsync();
                        try
                        {
                            var settings = new JsonSerializerSettings
                            {
                                TypeNameHandling = TypeNameHandling.Auto,
                                SerializationBinder = new SimpleBinder(),
                                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                                MissingMemberHandling = MissingMemberHandling.Ignore,
                                PreserveReferencesHandling = PreserveReferencesHandling.Objects,
                                NullValueHandling = NullValueHandling.Include
                            };
                            container = JsonConvert.DeserializeObject<SequenceRootContainer>(json, settings);
                        }
                        catch (Exception ex)
                        {
                            string inner = ex.InnerException != null ? ex.InnerException.Message : "";
                            statusCode = 500;
                            responseString = JsonConvert.SerializeObject(new { error = "Error deserializing sequence from payload.", details = ex.Message, innerException = inner });
                        }
                    }

                    if (container != null)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            _sequenceMediator.SetAdvancedSequence(container);
                        });
                        responseString = JsonConvert.SerializeObject(new { success = "Sequence updated successfully." });
                    }
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                responseString = JsonConvert.SerializeObject(new { error = "Internal server error", details = ex.Message });
            }
            finally
            {
                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
        }

        public class SimpleBinder : ISerializationBinder
        {
            public IList<Type> AllowedTypes { get; set; } = null;

            public Type BindToType(string assemblyName, string typeName)
            {
                // Optionally, you could restrict to allowed types.
                return Type.GetType($"{typeName}, {assemblyName}");
            }

            public void BindToName(Type serializedType, out string assemblyName, out string typeName)
            {
                assemblyName = serializedType.Assembly.FullName;
                typeName = serializedType.FullName;
            }
        }

        #endregion

        public override async Task Teardown()
        {
            _cts?.Cancel();
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
            }
            await base.Teardown();
        }

        #region INotifyPropertyChanged Members

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
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
