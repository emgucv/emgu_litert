// Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
#if ANDROID
using Android.Content;
using Android.Net.Nsd;
#elif IOS || MACCATALYST
using Foundation;
#endif

namespace Emgu.LiteRT.LM.Maui.OpenAI;

/// <summary>A Mac serving its chat's model, found on the network (<see cref="LocalServerBrowser"/>).</summary>
public sealed class FoundServer
{
    internal FoundServer(string name, string host, int port, string model)
    {
        Name = name;
        Host = host;
        Port = port;
        Model = model;
    }

    /// <summary>The Mac's name, e.g. "Canming's Mac mini".</summary>
    public string Name { get; }

    /// <summary>Its address or host name.</summary>
    public string Host { get; }

    /// <summary>The port it serves on.</summary>
    public int Port { get; }

    /// <summary>The model it serves (its name), or empty.</summary>
    public string Model { get; }

    /// <summary>The API's base address, e.g. "http://192.168.86.20:11435/v1".</summary>
    public string ApiUrl => "http://" + (Host.Contains(':') && !Host.StartsWith("[", StringComparison.Ordinal) ? "[" + Host + "]" : Host) + ":" + Port + "/v1";
}

/// <summary>
/// Finds the Macs on the network that serve their chat's model (<see cref="ServeModelsFeature"/>, announced with Bonjour as
/// <see cref="ServeModelsFeature.BonjourType"/>): Android's NsdManager, Apple's NSNetServiceBrowser (iOS asks the user for the
/// local network the first time, with the app's NSLocalNetworkUsageDescription; its Info.plist lists the type in
/// NSBonjourServices). Nothing elsewhere (<see cref="IsSupported"/>).
/// </summary>
public static class LocalServerBrowser
{
    /// <summary>Whether this platform can look for servers.</summary>
    public static bool IsSupported =>
#if ANDROID || IOS || MACCATALYST
        true;
#else
        false;
#endif

    /// <summary>The servers found within a time.</summary>
    /// <param name="time">How long to look</param>
    /// <param name="cancellationToken">Stops looking</param>
    public static async Task<IList<FoundServer>> BrowseAsync(TimeSpan time, CancellationToken cancellationToken = default)
    {
        var found = new List<FoundServer>();
#if ANDROID
        var manager = (NsdManager)Android.App.Application.Context.GetSystemService(Context.NsdService);
        var listener = new DiscoveryListener(manager, found);
        manager.DiscoverServices(ServeModelsFeature.BonjourType, NsdProtocol.DnsSd, listener);
        try { await Task.Delay(time, cancellationToken); }
        catch (OperationCanceledException) { }
        try { manager.StopServiceDiscovery(listener); }
        catch (Exception) { }
        await listener.ResolvedAsync(TimeSpan.FromSeconds(3));
#elif IOS || MACCATALYST
        var browser = new NSNetServiceBrowser();
        var services = new List<NSNetService>();
        browser.FoundService += (s, e) =>
        {
            NSNetService service = e.Service;
            services.Add(service);
            service.AddressResolved += (s2, e2) =>
            {
                string host = service.HostName?.TrimEnd('.');
                if (string.IsNullOrEmpty(host) || service.Port <= 0)
                    return;
                string model = string.Empty;
                try
                {
                    NSData txt = service.GetTxtRecordData();
                    if (txt != null && NSNetService.DictionaryFromTxtRecord(txt)?["model"] is NSData data)
                        model = NSString.FromData(data, NSStringEncoding.UTF8)?.ToString() ?? string.Empty;
                }
                catch (Exception) { }
                lock (found)
                    if (!found.Any(f => f.Name == service.Name))
                        found.Add(new FoundServer(service.Name, host, (int)service.Port, model));
            };
            service.Resolve(5);
        };
        await MainThread.InvokeOnMainThreadAsync(() => browser.SearchForServices(ServeModelsFeature.BonjourType + ".", "local."));
        try { await Task.Delay(time, cancellationToken); }
        catch (OperationCanceledException) { }
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            browser.Stop();
            foreach (NSNetService service in services)
                service.Stop();
        });
#else
        await Task.CompletedTask;
#endif
        lock (found)
            return found.ToList();
    }

#if ANDROID
    // Discovery, then each service resolved in turn (Android before 14 resolves one at a time).
    private sealed class DiscoveryListener : Java.Lang.Object, NsdManager.IDiscoveryListener
    {
        private readonly NsdManager _manager;
        private readonly List<FoundServer> _found;
        private readonly Queue<NsdServiceInfo> _pending = new Queue<NsdServiceInfo>();
        private bool _resolving;
        private TaskCompletionSource<bool> _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public DiscoveryListener(NsdManager manager, List<FoundServer> found)
        {
            _manager = manager;
            _found = found;
            _idle.TrySetResult(true);
        }

        public Task ResolvedAsync(TimeSpan timeout) => Task.WhenAny(_idle.Task, Task.Delay(timeout));

        public void OnServiceFound(NsdServiceInfo serviceInfo)
        {
            lock (_pending)
            {
                _pending.Enqueue(serviceInfo);
                if (_idle.Task.IsCompleted)
                    _idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            ResolveNext();
        }

        private void ResolveNext()
        {
            NsdServiceInfo next;
            lock (_pending)
            {
                if (_resolving)
                    return;
                if (_pending.Count == 0)
                {
                    _idle.TrySetResult(true);
                    return;
                }
                next = _pending.Dequeue();
                _resolving = true;
            }
#pragma warning disable CA1422 // ResolveService: obsolete on Android 14, where the callback API replaces it; still works.
            try { _manager.ResolveService(next, new ResolveListener(this)); }
            catch (Exception) { Resolved(null); }
#pragma warning restore CA1422
        }

        internal void Resolved(NsdServiceInfo info)
        {
            if (info?.Host != null && info.Port > 0)
            {
                string model = string.Empty;
                try
                {
                    if (info.Attributes != null && info.Attributes.TryGetValue("model", out byte[] bytes) && bytes != null)
                        model = System.Text.Encoding.UTF8.GetString(bytes);
                }
                catch (Exception) { }
                lock (_found)
                    if (!_found.Any(f => f.Name == info.ServiceName))
                        _found.Add(new FoundServer(info.ServiceName, info.Host.HostAddress, info.Port, model));
            }
            lock (_pending)
                _resolving = false;
            ResolveNext();
        }

        public void OnDiscoveryStarted(string serviceType) { }
        public void OnDiscoveryStopped(string serviceType) { }
        public void OnServiceLost(NsdServiceInfo serviceInfo) { }
        public void OnStartDiscoveryFailed(string serviceType, NsdFailure errorCode) => _idle.TrySetResult(true);
        public void OnStopDiscoveryFailed(string serviceType, NsdFailure errorCode) { }
    }

    private sealed class ResolveListener : Java.Lang.Object, NsdManager.IResolveListener
    {
        private readonly DiscoveryListener _owner;

        public ResolveListener(DiscoveryListener owner) => _owner = owner;

        public void OnResolveFailed(NsdServiceInfo serviceInfo, NsdFailure errorCode) => _owner.Resolved(null);

        public void OnServiceResolved(NsdServiceInfo serviceInfo) => _owner.Resolved(serviceInfo);
    }
#endif
}
