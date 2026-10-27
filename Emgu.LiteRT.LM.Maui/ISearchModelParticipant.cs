//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

namespace Emgu.LiteRT.LM.Maui
{
    /// <summary>
    /// A feature that uses the search model and must follow when the user switches it (<see cref="Features.SearchModelFeature"/>):
    /// an index it builds, a document it read. The switch finds them in the page's services
    /// (<see cref="ChatFeatureServices.GetAll{T}"/>); afterwards it tells the <see cref="IStorageParticipant"/>s that the
    /// storage changed, so they show their state again and start their work.
    /// </summary>
    public interface ISearchModelParticipant
    {
        /// <summary>
        /// Before the switch: stop the work that uses the model, and wait for it. Every participant is asked at once, so
        /// cancel first and wait after.
        /// </summary>
        Task PrepareSearchModelChangeAsync();

        /// <summary>The model was switched (and the old one let go of): rebuild what was made with it.</summary>
        /// <param name="otherModel">True for another model; false for the same model on another backend (its embeddings stay valid)</param>
        Task SearchModelChangedAsync(bool otherModel);
    }
}
