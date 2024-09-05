using Autofac.Core.Registration;
using Microsoft.Extensions.Configuration;

namespace Autofac.Annotation.Condition
{
    /// <summary>
    /// 条件里面配置的 根据配置文件进行配置
    /// </summary>
    internal class OnProperty : ICondition
    {
        /// <summary>
        /// true代表要过滤
        /// </summary>
        /// <param name="context"></param>
        /// <param name="config"></param>
        /// <returns></returns>
        public bool ShouldSkip(IComponentRegistryBuilder context, object config)
        {
            return config switch
            {
                ConditionalOnProperty metaConfig => matchSignleProperty(context, metaConfig),
                ConditionalOnProperties metaConfig2 => matchProperties(context, metaConfig2),
                _ => false
            };
        }

        private bool matchProperties(IComponentRegistryBuilder context, ConditionalOnProperties metaConfig)
        {
            var list = new List<string>();
            foreach (var name in metaConfig.names)
            {
                var temp = (metaConfig.prefix ?? "") + name;
                string value = null;
                try
                {
                    value = getConfiguration(context)[temp];
                }
                catch (Exception)
                {
                    //ignore
                }

                list.Add(value);
            }

            if (metaConfig.matchIfMissing)
            {
                var bools = list.Select(string.IsNullOrEmpty).Distinct().ToList();
                return bools.Count != 1 || !bools[0];
            }

            if (!string.IsNullOrEmpty(metaConfig.havingValue) && metaConfig.havingValue.Length > 0)
            {
                var bools2 = list.Select(r => r != null && r.Equals(metaConfig.havingValue)).Distinct().ToList();
                return bools2.Count != 1 || !bools2[0];
            }

            return list.Count(r => r != null) != metaConfig.names.Length;
        }

        private bool matchSignleProperty(IComponentRegistryBuilder context, ConditionalOnProperty metaConfig)
        {
            string value = null;
            try
            {
                value = getConfiguration(context)[metaConfig.name];
            }
            catch (Exception)
            {
                //ignore
            }

            if (metaConfig.matchIfMissing)
            {
                return !string.IsNullOrEmpty(value);
            }

            if (!string.IsNullOrEmpty(metaConfig.havingValue) && metaConfig.havingValue.Length > 0)
            {
                return !metaConfig.havingValue.Equals(value);
            }

            return value is null;
        }

        private IConfiguration getConfiguration(IComponentRegistryBuilder context)
        {
            if (context.Properties[nameof(AutofacAnnotationModule)] is AutofacAnnotationModule module)
            {
                var defaultValueResource = module.GetDefaultValueResource();
                if (defaultValueResource != null)
                {
                    return defaultValueResource.Value.Configuration;
                }
            }

            throw new InvalidOperationException("[OnProperty Condition] Get default configuration fail");
        }
    }
}