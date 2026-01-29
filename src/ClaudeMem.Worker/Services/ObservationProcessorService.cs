using ClaudeMem.Core.Repositories;

namespace ClaudeMem.Worker.Services;

public class ObservationProcessorService : BackgroundService
{
    private readonly IBackgroundQueue _queue;
    private readonly IClaudeService _claudeService;
    private readonly IObservationRepository _observationRepo;
    private readonly ISessionRepository _sessionRepo;
    private readonly ILogger<ObservationProcessorService> _logger;

    public ObservationProcessorService(
        IBackgroundQueue queue,
        IClaudeService claudeService,
        IObservationRepository observationRepo,
        ISessionRepository sessionRepo,
        ILogger<ObservationProcessorService> logger)
    {
        _queue = queue;
        _claudeService = claudeService;
        _observationRepo = observationRepo;
        _sessionRepo = sessionRepo;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Observation processor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var item = await _queue.DequeueObservationAsync(stoppingToken);
            if (item == null) continue;

            try
            {
                var session = _sessionRepo.GetByContentSessionId(item.ContentSessionId);
                var project = session?.Project ?? Path.GetFileName(item.Cwd);

                var observation = await _claudeService.ExtractObservationAsync(
                    item.ContentSessionId,
                    project,
                    item.ToolName,
                    item.ToolInput,
                    item.ToolResponse,
                    stoppingToken);

                if (observation != null)
                {
                    _observationRepo.Store(observation);
                    _logger.LogInformation("Created observation: {Title}", observation.Title);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process observation for session {SessionId}", item.ContentSessionId);
            }
        }
    }
}
