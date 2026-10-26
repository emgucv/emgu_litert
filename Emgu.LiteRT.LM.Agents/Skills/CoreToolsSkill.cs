//----------------------------------------------------------------------------
//  Copyright (C) 2004-2026 by EMGU Corporation. All rights reserved.
//----------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.AI;

namespace Emgu.LiteRT.LM.Agents.Skills
{
    /// <summary>
    /// The tools every assistant can use: the date and time, the calculator and unit conversion
    /// (<see cref="AgentTools.CurrentDateTime"/>, <see cref="AgentTools.Calculate"/>, <see cref="AgentTools.ConvertUnits"/>).
    /// </summary>
    public sealed class CoreToolsSkill : ChatSkill
    {
        /// <inheritdoc/>
        public override string Name => "core";

        /// <inheritdoc/>
        public override string Title => "Date, calculator and units";

        /// <inheritdoc/>
        public override IEnumerable<AITool> Tools(ChatMessageContext context) =>
            new AITool[] { AgentTools.CurrentDateTime(), AgentTools.Calculate(), AgentTools.ConvertUnits() };

        /// <inheritdoc/>
        public override string ToolSummary => "date and time, calculator, unit conversion";
    }
}
