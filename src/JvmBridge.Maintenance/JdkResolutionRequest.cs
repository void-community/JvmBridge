using JvmBridge.Build.Models;

namespace JvmBridge.Maintenance;

internal sealed record JdkResolutionRequest(TargetConfiguration Target, int Major, string Implementation);
