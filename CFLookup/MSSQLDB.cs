using Microsoft.Data.SqlClient;
using System.Data;

namespace CFLookup
{
    public class MSSQLDB : IDisposable
    {
        private readonly SqlConnection _connection;

        public MSSQLDB(SqlConnection connection)
        {
            _connection = connection;
        }

        public async Task<DataTable> ExecuteDataTableAsync(string sql, params SqlParameter[] parameters)
        {
            await EnsureConnected();
            var command = GetCommandWithParams(sql, parameters);

            using (var da = new SqlDataAdapter(command))
            {
                DataTable dt = new DataTable();

                da.Fill(dt);

                return dt;
            }
        }

        public async Task<List<T>> ExecuteListAsync<T>(string sql, params SqlParameter[] parameters)
        {
            var dt = await ExecuteDataTableAsync(sql, parameters);

            List<T> items = new List<T>();

            foreach (DataRow row in dt.Rows)
            {
                T item = (T)Activator.CreateInstance(typeof(T), row);
                items.Add(item);
            }

            return items;
        }

        public async Task<T> ExecuteSingleRowAsync<T>(string sql, params SqlParameter[] parameters)
        {
            var rows = await ExecuteListAsync<T>(sql, parameters);

            return rows.FirstOrDefault();
        }

        public async Task<int> ExecuteNonQueryAsync(string sql, params SqlParameter[] parameters)
        {
            await EnsureConnected();
            var command = GetCommandWithParams(sql, parameters);

            return await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ExecuteScalarAsync<T>(string sql, params SqlParameter[] parameters)
        {
            await EnsureConnected();
            var command = GetCommandWithParams(sql, parameters);

            var retValue = await command.ExecuteScalarAsync();

            if (retValue is T)
                return (T)retValue;

            return default;
        }

        private SqlCommand GetCommandWithParams(string sql, SqlParameter[] parameters)
        {
            var command = _connection.CreateCommand();
            command.CommandTimeout = 300;
            command.CommandText = sql;
            command.Parameters.AddRange(parameters);

            return command;
        }

        private async Task EnsureConnected()
        {
            if (_connection.State == ConnectionState.Closed)
            {
                await _connection.OpenAsync();
            }
        }

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
        }

        public void Dispose(bool isDisposing)
        {
            if (isDisposing) Dispose();
        }
    }
}
