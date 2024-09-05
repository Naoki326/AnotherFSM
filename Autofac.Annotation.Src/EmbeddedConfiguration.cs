using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Reflection;

namespace Autofac.Annotation
{
    /// <summary>
    /// 资源
    /// </summary>
    internal static class EmbeddedConfiguration
    {
        /// <summary>
        /// 缓存 key为path
        /// </summary>
        private static ConcurrentDictionary<string, IConfiguration> Configurations = new ConcurrentDictionary<string, IConfiguration>();

        /// <summary>
        /// LoadEmbedded
        /// </summary>
        /// <param name="type"></param>
        /// <param name="configFile"></param>
        /// <param name="sourceType"></param>
        /// <param name="isEmbedded"></param>
        /// <param name="isReload"></param>
        /// <returns></returns>
        public static IConfiguration Load(Type type, string configFile, MetaSourceType sourceType, bool isEmbedded = false, bool isReload = true)
        {
            if (Configurations.TryGetValue(configFile, out var con))
            {
                return con;
            }

            if (!isEmbedded && !File.Exists(configFile))
            {
                return null;
            }


            if (isEmbedded || !isReload)
            {
                using (var stream = isEmbedded ? GetEmbeddedFileStream(type, configFile) : File.OpenRead(configFile))
                {
                    if (sourceType.Equals(MetaSourceType.Auto))
                    {
                        throw new NotSupportedException($"source file can not parse $`{configFile}`");
                    }
                }
            }


            if (sourceType.Equals(MetaSourceType.Auto))
            {
                throw new NotSupportedException($"source file can not parse $`{configFile}`");
            }
            return null;
        }

        private static Stream GetEmbeddedFileStream(Type type, string configFile)
        {
            try
            {
                var stream = type.GetTypeInfo().Assembly.GetManifestResourceStream(configFile);
                if (stream == null)
                {
                    throw new FileNotFoundException($"embedded file ‘{configFile}’ not find in type : {type.FullName}");
                }

                return stream;
            }
            catch (Exception)
            {
                throw new FileNotFoundException($"embedded file ‘{configFile}’ not find in type : {type.FullName}");
            }
        }
    }
}