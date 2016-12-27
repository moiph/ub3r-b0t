namespace UB3RB0T
{
    using Newtonsoft.Json;
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Net;
    using System.Threading.Tasks;

    internal class JsonConfig
    {
        public const string PathEnvironmentVariableName = "uberconfigpath";

        internal static readonly ConcurrentDictionary<string, object> ConfigInstances = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public static T AddOrUpdateInstance<T>(string key, object addValue, Func<string, object, object> valueFactory) where T: class, new()
        {
            object instance = ConfigInstances.AddOrUpdate(key, addValue, valueFactory);
            return (T)instance;
        }

        public static T GetOrAddInstance<T>(string key, Func<string, object> valueFactory) where T : class, new()
        {
            object instance = ConfigInstances.GetOrAdd(key, valueFactory);
            return (T)instance;
        }
    }

    public abstract class JsonConfig<T> where T : JsonConfig<T>, new()
    {
        private static readonly string instanceKey = typeof(T).ToString();

        protected virtual string FileName { get; }

        public static T Instance
        {
            get
            {
                return JsonConfig.GetOrAddInstance<T>(instanceKey, (x) => Parse());
            }
        }

        public void Reset()
        {
            JsonConfig.ConfigInstances.TryRemove(instanceKey, out object oldConfig);
        }

        public async Task OverrideAsync(Uri uri)
        {
            string content = string.Empty;
            var req = WebRequest.Create(uri);
            WebResponse webResponse = null;
            webResponse = await req.GetResponseAsync();

            if (webResponse != null)
            {
                Stream responseStream = webResponse.GetResponseStream();
                using (StreamReader reader = new StreamReader(responseStream))
                {
                    content = await reader.ReadToEndAsync();
                }

            }

            var config = JsonConvert.DeserializeObject<T>(content);
            JsonConfig.AddOrUpdateInstance<T>(instanceKey, config, (x, y) => config);
        }

        private static T Parse()
        {
            var config = new T();
            string contents = string.Empty;

            if (!string.IsNullOrEmpty(config.FileName))
            {
                string rootPath = Environment.GetEnvironmentVariable(JsonConfig.PathEnvironmentVariableName) ?? "Config\\";
                contents = File.ReadAllText(Path.Combine(rootPath, config.FileName));
            }
            else
            {
                throw new InvalidConfigException("Config must specify either a filename or a URI");
            }

            return JsonConvert.DeserializeObject<T>(contents);
        }
    }
}