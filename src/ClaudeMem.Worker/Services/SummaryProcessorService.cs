using ClaudeMem.Core.Models;
using ClaudeMem.Core.Repositories;

namespace ClaudeMem.Worker.Services;

public class SummaryProcessorService : BackgroundService
{
    private readonly IBackgroundQueue _queue;
    private readonly IClaudeService _claudeService;
    private readonly IObservationRepository _observationRepo;
    private readonly ISessionRepository _sessionRepo;
    private readonly ISummaryRepository _summaryRepo;
    private readonly ILogger<SummaryProcessorService> _logger;

    public SummaryProcessorService(
        IBackgroundQueue queue,
        IClaudeService claudeService,
        IObservationRepository observationRepo,
        ISessionRepository sessionRepo,
        ISummaryRepository summaryRepo,
        ILogger<SummaryProcessorService> logger)
    {
        _queue = queue;
        _claudeService = claudeService;
        _observationRepo = observationRepo;
        _sessionRepo = sessionRepo;
        _summaryRepo = summaryRepo;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Summary processor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var item = await _queue.DequeueSummaryAsync(stoppingToken);
            if (item == null) continue;

            try
            {
                var session = _sessionRepo.GetByContentSessionId(item.ContentSessionId);
                if (session == null)
                {
                    _logger.LogWarning("Session not found: {SessionId}", item.ContentSessionId);
                    continue;
                }

                var observations = _observationRepo.GetBySessionId(item.ContentSessionId);
                if (observations.Count == 0)
                {
                    _logger.LogInformation("No observations to summarize for session {SessionId}", item.ContentSessionId);
                    continue;
                }

                var extraction = await _claudeService.GenerateSummaryAsync(
                    item.ContentSessionId,
                    observations,
                    item.LastAssistantMessage,
                    stoppingToken);

                if (extraction != null)
                {
                    var summary = new Summary
                    {
                        MemorySessionId = item.ContentSessionId,
                        Project = session.Project,
                        Request = extraction.Request,
                        Investigated = extraction.Investigated,
                        Learned = extraction.Learned,
                        Completed = extraction.Completed,
                        NextSteps = extraction.NextSteps,
                        FilesRead = extraction.FilesRead != null ? string.Join(", ", extraction.FilesRead) : null,
                        FilesEdited = extraction.FilesEdited != null ? string.Join(", ", extraction.FilesEdited) : null,
                        Notes = extraction.Notes,
                        CreatedAt = DateTime.UtcNow
                    };

                    _summaryRepo.Store(summary);
                    _logger.LogInformation("Created summary for session {SessionId}", item.ContentSessionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate summary for session {SessionId}", item.ContentSessionId);
            }
        }
    }
}
