//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Emgu.LiteRT.LM.Server
{
    /// <summary>A device paired with a <see cref="ChatServer"/>: its name and its token's hash, never the token.</summary>
    public sealed class PairedDevice
    {
        /// <summary>A paired device.</summary>
        /// <param name="id">Its id (random)</param>
        /// <param name="name">Its name, as it gave it when pairing, e.g. "Galaxy S10"</param>
        /// <param name="tokenHash">Its token's SHA-256, in hexadecimal</param>
        /// <param name="paired">When it paired (UTC)</param>
        public PairedDevice(string id, string name, string tokenHash, DateTime paired)
        {
            Id = id;
            Name = name;
            TokenHash = tokenHash;
            Paired = paired;
        }

        /// <summary>Its id.</summary>
        public string Id { get; }

        /// <summary>Its name.</summary>
        public string Name { get; }

        /// <summary>Its token's SHA-256, in hexadecimal.</summary>
        public string TokenHash { get; }

        /// <summary>When it paired (UTC).</summary>
        public DateTime Paired { get; }

        /// <summary>When it last made a request (UTC), or null.</summary>
        public DateTime? LastUsed { get; set; }
    }

    /// <summary>Where a <see cref="ServerPairing"/> keeps the paired devices (e.g. an app's settings).</summary>
    public interface IServerTokenStore
    {
        /// <summary>The paired devices.</summary>
        IList<PairedDevice> Load();

        /// <summary>Keep the paired devices.</summary>
        /// <param name="devices">All of them</param>
        void Save(IList<PairedDevice> devices);
    }

    /// <summary>A <see cref="IServerTokenStore"/> in memory (tests, a console host): forgotten when the process ends.</summary>
    public sealed class InMemoryServerTokenStore : IServerTokenStore
    {
        private List<PairedDevice> _devices = new List<PairedDevice>();

        /// <inheritdoc/>
        public IList<PairedDevice> Load() => _devices.ToList();

        /// <inheritdoc/>
        public void Save(IList<PairedDevice> devices) => _devices = devices.ToList();
    }

    /// <summary>
    /// Pairing devices with a <see cref="ChatServer"/>: the server shows a 6-digit code (<see cref="NewCode"/>, valid for
    /// <see cref="CodeLifetime"/>), a device sends it with its name (POST /pair) and gets a token - 32 random bytes - that it
    /// sends with every request. Only the token's hash is kept. A code ends after its lifetime, after it is used, or after
    /// <see cref="MaxWrongCodes"/> wrong ones. Thread-safe.
    /// </summary>
    public sealed class ServerPairing
    {
        /// <summary>How long a code is valid.</summary>
        public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

        /// <summary>The wrong codes after which the code ends (a new one has to be shown).</summary>
        public const int MaxWrongCodes = 5;

        private readonly IServerTokenStore _store;
        private readonly Func<DateTime> _utcNow;
        private readonly object _lock = new object();
        private List<PairedDevice> _devices;
        private string _code;
        private DateTime _codeExpires;
        private int _wrong;
        private DateTime _lastSaved;

        /// <summary>Pair devices, keeping them in a store.</summary>
        /// <param name="store">Where the paired devices are kept</param>
        /// <param name="utcNow">The current UTC time (for tests), or null for DateTime.UtcNow</param>
        public ServerPairing(IServerTokenStore store, Func<DateTime> utcNow = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _devices = (store.Load() ?? new List<PairedDevice>()).ToList();
        }

        /// <summary>Raised (on any thread) when a device paired or was removed.</summary>
        public event EventHandler Changed;

        /// <summary>The paired devices.</summary>
        public IReadOnlyList<PairedDevice> Devices
        {
            get { lock (_lock) return _devices.ToList(); }
        }

        /// <summary>A new 6-digit code for pairing (the last one ends), valid for <see cref="CodeLifetime"/>.</summary>
        public string NewCode()
        {
            lock (_lock)
            {
                _code = RandomNumber(1000000).ToString("D6");
                _codeExpires = _utcNow() + CodeLifetime;
                _wrong = 0;
                return _code;
            }
        }

        /// <summary>The code shown now, or null when there is none (used, expired, too many wrong ones).</summary>
        public string CurrentCode
        {
            get
            {
                lock (_lock)
                    return _code != null && _utcNow() < _codeExpires ? _code : null;
            }
        }

        /// <summary>When the current code ends (UTC), or null.</summary>
        public DateTime? CodeExpires
        {
            get { lock (_lock) return CurrentCode != null ? _codeExpires : (DateTime?)null; }
        }

        /// <summary>End the current code (the pairing sheet was closed).</summary>
        public void CancelCode()
        {
            lock (_lock)
                _code = null;
        }

        /// <summary>
        /// Pair a device with the code it was given: its token, or null for a wrong or ended code (the fifth wrong one ends
        /// the code).
        /// </summary>
        /// <param name="code">The code the device sent</param>
        /// <param name="deviceName">The device's name, e.g. "Galaxy S10"</param>
        public string TryPair(string code, string deviceName)
        {
            string token;
            lock (_lock)
            {
                if (_code == null || _utcNow() >= _codeExpires)
                {
                    _code = null;
                    return null;
                }
                if (!FixedTimeEquals((code ?? string.Empty).Trim(), _code))
                {
                    if (++_wrong >= MaxWrongCodes)
                        _code = null;
                    return null;
                }
                _code = null;
                token = Base64Url(RandomBytes(32));
                string name = string.IsNullOrWhiteSpace(deviceName) ? "A device" : deviceName.Trim();
                if (name.Length > 60)
                    name = name.Substring(0, 60);
                _devices.Add(new PairedDevice(Base64Url(RandomBytes(9)), name, Hash(token), _utcNow()));
                _store.Save(_devices);
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return token;
        }

        /// <summary>The device a token belongs to (its last use noted), or null.</summary>
        /// <param name="token">The token from the request</param>
        public PairedDevice Validate(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;
            string hash = Hash(token);
            lock (_lock)
            {
                PairedDevice device = _devices.FirstOrDefault(d => FixedTimeEquals(d.TokenHash, hash));
                if (device == null)
                    return null;
                DateTime now = _utcNow();
                device.LastUsed = now;
                // Not on every request: the last use is for the settings' list.
                if (now - _lastSaved > TimeSpan.FromMinutes(1))
                {
                    _lastSaved = now;
                    _store.Save(_devices);
                }
                return device;
            }
        }

        /// <summary>Remove a paired device: its token stops working.</summary>
        /// <param name="id">The device's id</param>
        /// <returns>False if there is none</returns>
        public bool Remove(string id)
        {
            lock (_lock)
            {
                if (_devices.RemoveAll(d => d.Id == id) == 0)
                    return false;
                _store.Save(_devices);
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private static string Hash(string token)
        {
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(token)).Select(b => b.ToString("x2")));
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static byte[] RandomBytes(int count)
        {
            byte[] bytes = new byte[count];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            return bytes;
        }

        // A uniform random number below max.
        private static int RandomNumber(int max)
        {
            uint limit = uint.MaxValue - uint.MaxValue % (uint)max;
            while (true)
            {
                uint value = BitConverter.ToUInt32(RandomBytes(4), 0);
                if (value < limit)
                    return (int)(value % (uint)max);
            }
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
