using StackExchange.Redis;

namespace CFLookup
{
    public sealed class RedisJobLock : IAsyncDisposable
    {
        private readonly TimeSpan _expiryTime;
        private string _lockKey { get; }

        private IDatabase _redisDatabase { get; }
        private readonly string _lockOwner;
        private readonly CancellationTokenSource _cts;
        private Task? _refreshLockTask;

        private RedisJobLock(IDatabase database, string lockName, TimeSpan expiryTime)
        {
            _expiryTime = expiryTime;
            _redisDatabase = database;

            _lockOwner = Guid.NewGuid().ToString();

            _lockKey = $"joblock:{lockName}";
            _cts = new CancellationTokenSource();
        }

        public async static Task<RedisJobLock?> CreateAsync(
            IDatabase database,
            string lockName,
            TimeSpan expiryTime
        )
        {
            var distributedLock = new RedisJobLock(database, lockName, expiryTime);

            var lockAcquired = await database.LockTakeAsync(
                distributedLock._lockKey,
                distributedLock._lockOwner,
                distributedLock._expiryTime);

            if (lockAcquired)
            {
                distributedLock.StartRenewalTask();
                //logger.LogDebug("Lock acquired for key {LockKey} by owner {LockOwner}", distributedLock._lockKey, distributedLock._lockOwner);
                return distributedLock;
            }
            
            //logger.LogDebug("Failed to acquire lock for key {LockKey}", distributedLock._lockKey);
            return null;
        }

        private void StartRenewalTask()
        {
            _refreshLockTask = Task.Run(async () =>
            {
                var renewalDelay = TimeSpan.FromMilliseconds(_expiryTime.TotalMilliseconds / 2.5);

                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(renewalDelay, _cts.Token);

                        var renewed = await _redisDatabase.LockExtendAsync(_lockKey, _lockOwner, _expiryTime);

                        if (renewed)
                        {
                            //_logger.LogDebug("Renewed lock for key {LockKey}", _lockKey);
                        }
                        else
                        {
                            //_logger.LogError("Failed to renew lock for key {LockKey}. Lock has been lost.", _lockKey);
                            break;
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        //_logger.LogError(ex, "Failed to renew lock for key {LockKey} due to exception.", _lockKey);
                        break;
                    }
                }
            });
        }

        public async ValueTask DisposeAsync()
        {
            if (_refreshLockTask == null)
            {
                return;
            }
            
            //_logger.LogDebug($"Releasing lock {_lockKey} / {_lockOwner}");

            if (!_cts.IsCancellationRequested)
            {
                await _cts.CancelAsync();
            }

            var released = await _redisDatabase.LockReleaseAsync(_lockKey, _lockOwner);
            if (!released)
            {
                //_logger.LogWarning("Failed to release lock for key {LockKey}, it may have expired already", _lockKey);
            }
            else
            {
               // _logger.LogDebug("Released lock for key {LockKey}", _lockKey);
            }

            try
            {
                await _refreshLockTask;
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex, "Failed to refresh lock for key {LockKey}", _lockKey);
            }
            
            _cts.Dispose();
        }
    }
}