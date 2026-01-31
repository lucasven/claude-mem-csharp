# Deployment

## Linux (systemd)

1. Build and publish:
```bash
dotnet publish src/ClaudeMem.Worker -c Release -r linux-x64 --self-contained -o /opt/claude-mem
```

2. Copy the service file:
```bash
sudo cp deploy/claude-mem-worker.service /etc/systemd/system/
```

3. Enable and start:
```bash
sudo systemctl daemon-reload
sudo systemctl enable claude-mem-worker
sudo systemctl start claude-mem-worker
```

4. Check status:
```bash
sudo systemctl status claude-mem-worker
journalctl -u claude-mem-worker -f
```

## Docker (coming soon)

A Dockerfile will be added for containerized deployments.

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `CLAUDE_MEM_WORKER_PORT` | 37777 | HTTP port to listen on |
| `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` | 1 | Required for self-contained Linux builds |
