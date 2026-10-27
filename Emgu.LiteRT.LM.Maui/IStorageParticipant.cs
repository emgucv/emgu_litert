//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Chat;

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// A feature with a stake in what the storage feature (<see cref="Features.StorageFeature"/>) deletes or downloads
    /// again - an index it builds, a document it is reading, a model it holds. The storage feature finds them in the
    /// page's services (<see cref="ChatFeatureServices.GetAll{T}"/>; register with
    /// <c>services.Add&lt;IStorageParticipant&gt;(this)</c> too when the feature registers itself by its own type).
    /// </summary>
    public interface IStorageParticipant
    {
        /// <summary>
        /// Before an entry is deleted: stop the work that uses it, and let go of what holds it (an index being built
        /// is cancelled and waited for, a document being read is removed, a switch is turned off). Every participant is
        /// asked at once, so cancel first and wait after: the work may share one model load.
        /// </summary>
        /// <param name="entry">What is about to be deleted</param>
        Task PrepareStorageDeleteAsync(StorageEntry entry);

        /// <summary>
        /// Storage changed: an entry was deleted or a search model downloaded again, or only the search model's
        /// availability changed. Show the state again, and start the work that may go on now.
        /// </summary>
        /// <param name="entry">What was deleted or downloaded; null when only the search model's availability changed</param>
        void StorageChanged(StorageEntry entry);
    }
}
