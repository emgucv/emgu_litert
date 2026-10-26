//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using Emgu.LiteRT.LM.Agents.Skills;

namespace Emgu.LiteRT.LM.Maui.Features
{
    /// <summary>The date and time, the calculator and unit conversion (<see cref="CoreToolsSkill"/>); always on, no settings.</summary>
    public sealed class CoreToolsFeature : SkillFeature<CoreToolsSkill>
    {
        /// <summary>Create the feature.</summary>
        public CoreToolsFeature() : base(new CoreToolsSkill())
        {
        }

        /// <inheritdoc/>
        public override bool PersistEnabled => false;
    }
}
