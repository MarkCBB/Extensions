// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Internal;

namespace Microsoft.Extensions.Caching.SqlServer
{
    internal class MonoDatabaseOperations : DatabaseOperations
    {
        public MonoDatabaseOperations(
            string connectionString, string schemaName, string tableName, ISystemClock systemClock)
            : base(connectionString, schemaName, tableName, systemClock)
        {
        }

        public override byte[] GetCacheItem(string key)
        {
            var utcNow = SystemClock.UtcNow;

            using (var connection = new SqlConnection(ConnectionString))
            using (var command = new SqlCommand(SqlQueries.GetCacheItem, connection))
            {
                command.Parameters
                    .AddCacheItemId(key)
                    .AddWithValue("UtcNow", SqlDbType.DateTime, utcNow.UtcDateTime);

                connection.Open();

                var reader = command.ExecuteReader(CommandBehavior.SingleRow | CommandBehavior.SingleResult);

                if (reader.Read())
                {
                    return (byte[])reader[Columns.Indexes.CacheItemValueIndex];
                }
                else
                {
                    return null;
                }
            }
        }

        public override async Task<byte[]> GetCacheItemAsync(string key, CancellationToken token = default(CancellationToken))
        {
            token.ThrowIfCancellationRequested();

            var utcNow = SystemClock.UtcNow;

            using (var connection = new SqlConnection(ConnectionString))
            using (var command = new SqlCommand(SqlQueries.GetCacheItem, connection))
            {
                command.Parameters
                    .AddCacheItemId(key)
                    .AddWithValue("UtcNow", SqlDbType.DateTime, utcNow.UtcDateTime);

                await connection.OpenAsync(token).ConfigureAwait(false);

                var reader = await command.ExecuteReaderAsync(
                    CommandBehavior.SingleRow | CommandBehavior.SingleResult,
                    token).ConfigureAwait(false);

                if (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    return (byte[])reader[Columns.Indexes.CacheItemValueIndex];
                }
                else
                {
                    return null;
                }
            }
        }

        public override void SetCacheItem(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            var utcNow = SystemClock.UtcNow;

            var absoluteExpiration = GetAbsoluteExpiration(utcNow, options);
            ValidateOptions(options.SlidingExpiration, absoluteExpiration);

            using (var connection = new SqlConnection(ConnectionString))
            using (var upsertCommand = new SqlCommand(SqlQueries.SetCacheItem, connection))
            {
                upsertCommand.Parameters
                    .AddCacheItemId(key)
                    .AddCacheItemValue(value)
                    .AddSlidingExpirationInSeconds(options.SlidingExpiration)
                    .AddAbsoluteExpirationMono(absoluteExpiration)
                    .AddWithValue("UtcNow", SqlDbType.DateTime, utcNow.UtcDateTime);

                connection.Open();

                try
                {
                    upsertCommand.ExecuteNonQuery();
                }
                catch (SqlException ex)
                {
                    if (IsDuplicateKeyException(ex))
                    {
                        // There is a possibility that multiple requests can try to add the same item to the cache, in
                        // which case we receive a 'duplicate key' exception on the primary key column.
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        public override async Task SetCacheItemAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default(CancellationToken))
        {
            token.ThrowIfCancellationRequested();

            var utcNow = SystemClock.UtcNow;

            var absoluteExpiration = GetAbsoluteExpiration(utcNow, options);
            ValidateOptions(options.SlidingExpiration, absoluteExpiration);

            using (var connection = new SqlConnection(ConnectionString))
            using (var upsertCommand = new SqlCommand(SqlQueries.SetCacheItem, connection))
            {
                upsertCommand.Parameters
                    .AddCacheItemId(key)
                    .AddCacheItemValue(value)
                    .AddSlidingExpirationInSeconds(options.SlidingExpiration)
                    .AddAbsoluteExpirationMono(absoluteExpiration)
                    .AddWithValue("UtcNow", SqlDbType.DateTime, utcNow.UtcDateTime);

                await connection.OpenAsync(token).ConfigureAwait(false);

                try
                {
                    await upsertCommand.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
                catch (SqlException ex)
                {
                    if (IsDuplicateKeyException(ex))
                    {
                        // There is a possibility that multiple requests can try to add the same item to the cache, in
                        // which case we receive a 'duplicate key' exception on the primary key column.
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        public override void RefreshCacheItem(string key)
        {
            var utcNow = SystemClock.UtcNow;

            using (var connection = new SqlConnection(ConnectionString))
            using (var command = new SqlCommand(SqlQueries.UpdateExpirationDate, connection))
            {
                command.Parameters
                    .AddCacheItemId(key)
                    .AddWithValue("UtcNow", SqlDbType.DateTime, utcNow.UtcDateTime);

                connection.Open();

                command.ExecuteNonQuery();
            }
        }

        public override async Task RefreshCacheItemAsync(string key, CancellationToken token = default(CancellationToken))
        {
            token.ThrowIfCancellationRequested();

            var utcNow = SystemClock.UtcNow;

            using (var connection = new SqlConnection(ConnectionString))
            using (var command = new SqlCommand(SqlQueries.UpdateExpirationDate, connection))
            {
                command.Parameters
                    .AddCacheItemId(key)
                    .AddWithValue("UtcNow", SqlDbType.DateTime, utcNow.UtcDateTime);

                await connection.OpenAsync(token).ConfigureAwait(false);

                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }


        public override void DeleteExpiredCacheItems()
        {
            var utcNow = SystemClock.UtcNow;

            using (var connection = new SqlConnection(ConnectionString))
            using (var command = new SqlCommand(SqlQueries.DeleteExpiredCacheItems, connection))
            {
                command.Parameters.AddWithValue("UtcNow", SqlDbType.DateTime, utcNow.UtcDateTime);

                connection.Open();

                var effectedRowCount = command.ExecuteNonQuery();
            }
        }
    }
}
