using Microsoft.Maui.AI.Attributes;

namespace DotNetConf.App.Services;

[AIToolSource(typeof(PlannerTools))]
public partial class PlannerToolContext : AIToolContext;
