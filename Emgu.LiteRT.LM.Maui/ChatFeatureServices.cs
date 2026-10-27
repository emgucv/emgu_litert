//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// Services the features of a <see cref="ChatPage"/> offer each other, by type (e.g. a camera feature's visual agent
    /// registry, which an exercise feature registers its agents with): <see cref="ChatFeature.RegisterServices"/> adds them,
    /// <see cref="Get{T}"/> finds them.
    /// </summary>
    public sealed class ChatFeatureServices
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        /// <summary>Add a service (replacing one of the same type).</summary>
        /// <typeparam name="T">The type it is found by</typeparam>
        /// <param name="service">The service</param>
        public void Add<T>(T service) where T : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));
            _services[typeof(T)] = service;
        }

        /// <summary>The service of a type, or null.</summary>
        /// <typeparam name="T">The type</typeparam>
        public T Get<T>() where T : class => _services.TryGetValue(typeof(T), out object service) ? service as T : null;

        /// <summary>Every service that is a <typeparamref name="T"/>, whatever type it was added by (e.g. the <see cref="IStorageParticipant"/>s).</summary>
        /// <typeparam name="T">The type or interface</typeparam>
        public IReadOnlyList<T> GetAll<T>() where T : class => _services.Values.OfType<T>().Distinct().ToList();
    }
}
