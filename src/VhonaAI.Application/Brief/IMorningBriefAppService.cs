using VhonaAI.Core.Brief;

namespace VhonaAI.Application.Brief;

public interface IMorningBriefAppService
{
    Task<MorningBriefPreferenceState> GetPreferenceAsync(CancellationToken cancellationToken = default);
    Task<MorningBriefOptInResult> OptInAsync(CancellationToken cancellationToken = default);
    Task<MorningBriefPreferenceState> DismissAsync(CancellationToken cancellationToken = default);
    Task<MorningBriefLanding> GetLandingAsync(CancellationToken cancellationToken = default);
    Task<MorningBriefLanding> EnsureTodaysBriefAsync(CancellationToken cancellationToken = default);
}
