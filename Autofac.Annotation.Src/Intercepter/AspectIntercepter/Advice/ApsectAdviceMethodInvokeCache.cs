using Autofac.Annotation;
using Autofac.Annotation.Util;
using System.Collections.Concurrent;
using System.Reflection;

namespace Autofac.AspectIntercepter.Advice
{
    /// <summary>
    ///     AOP拦截器方法Attribute的缓存
    ///     在DI容器build的时候会触发这个实例new
    ///     然后解析所有打了Aspect标签的class进行解析打了有继承AspectInvokeAttribute的所有方法并且缓存起来
    /// </summary>
    [Component(Services = new[] { typeof(ApsectAdviceMethodInvokeCache), typeof(IStartable) }, AutofacScope = AutofacScope.SingleInstance, AutoActivate = true,
        NotUseProxy = true, OrderIndex = int.MinValue)]
    public class ApsectAdviceMethodInvokeCache : IStartable
    {
        /// <summary>
        ///     构造方法
        /// </summary>
        public ApsectAdviceMethodInvokeCache(IComponentContext context)
        {
            CacheList = new ConcurrentDictionary<MethodInfo, AspectAttributeChainBuilder>();
            DynamicCacheList = new ConcurrentDictionary<string, AspectAttributeChainBuilder>();
            var componentModelCacheSingleton = context.Resolve<ComponentModelCacheSingleton>();
            foreach (var aspectClass in componentModelCacheSingleton.ComponentModelCache.Values)
            {
                AddCacheInter(aspectClass);
            }
        }

        /// <summary>
        /// 给外部用
        /// </summary>
        /// <param name="aspectClass"></param>
        internal void AddCache(ComponentModel aspectClass)
        {
            lock (aspectClass.AspectAttributeCache)
            {
                AddCacheInter(aspectClass, aspectClass.isGeneric);
            }
        }

        private void AddCacheInter(ComponentModel aspectClass, bool isGeneric = false)
        {
            if (!aspectClass.AspectAttributeCache.Any()) return;

            foreach (var item in aspectClass.AspectAttributeCache)
            {
                var method = item.Key;
                var attributes = item.Value;

                if (!attributes.Any()) continue;

                var aspectAttributeInfo = new AspectAttributeChainBuilder
                {
                    AdviceMethod = new List<AdviceMethod>()
                };

                var allGroupNameList = new Dictionary<string, string>();
                var beforeCache = new Dictionary<string, AspectBefore>();
                var afterCache = new Dictionary<string, AspectAfter>();
                var afterReturnCache = new Dictionary<string, AspectAfterReturn>();
                var afterThrow = new Dictionary<string, AspectAfterThrows>();
                var arround = new Dictionary<string, AspectArround>();

                foreach (var attribute in attributes)
                {
                    var key = attribute.GroupName ?? "";
                    switch (attribute)
                    {
                        case AspectBefore aspectBeforeAttribute:
                            if (beforeCache.ContainsKey(key))
                            {
                                //当默认的添加满的时候 自动分组
                                if (string.IsNullOrEmpty(key)) key = attribute.GetType().FullName;

                                if (beforeCache.ContainsKey(key))
                                    throw new InvalidOperationException(
                                        $"The Aspect target class `{aspectClass.CurrentType.Namespace + "." + aspectClass.CurrentType.Name}` method $`{method.Name}` can not be register multi [AspectBefore]${(!string.IsNullOrEmpty(key) ? " with key:`" + key + "`" : "")}!");
                            }

                            beforeCache.Add(key, aspectBeforeAttribute);
                            if (!allGroupNameList.ContainsKey(key)) allGroupNameList.Add(key, string.Empty);
                            break;
                        case AspectAfter aspectAfter:
                            if (afterCache.ContainsKey(key))
                            {
                                //当默认的添加满的时候 自动分组
                                if (string.IsNullOrEmpty(key)) key = attribute.GetType().FullName;

                                if (afterCache.ContainsKey(key))
                                    throw new InvalidOperationException(
                                        $"The Aspect target class `{aspectClass.CurrentType.Namespace + "." + aspectClass.CurrentType.Name}` method $`{method.Name}` can not be register multi [AspectAfter]${(!string.IsNullOrEmpty(key) ? " with key:`" + key + "`" : "")}!");
                            }

                            afterCache.Add(key, aspectAfter);
                            if (!allGroupNameList.ContainsKey(key)) allGroupNameList.Add(key, string.Empty);
                            break;
                        case AspectAfterReturn aspectAfterAttribute:
                            if (afterReturnCache.ContainsKey(key))
                            {
                                //当默认的添加满的时候 自动分组
                                if (string.IsNullOrEmpty(key)) key = attribute.GetType().FullName;

                                if (afterReturnCache.ContainsKey(key))
                                    throw new InvalidOperationException(
                                        $"The Aspect target class `{aspectClass.CurrentType.Namespace + "." + aspectClass.CurrentType.Name}` method $`{method.Name}` can not be register multi [AspectAfterReturn]${(!string.IsNullOrEmpty(key) ? " with key:`" + key + "`" : "")}!");
                            }

                            afterReturnCache.Add(key, aspectAfterAttribute);
                            if (!allGroupNameList.ContainsKey(key)) allGroupNameList.Add(key, string.Empty);
                            break;
                        case AspectAfterThrows aspectAfterThrowing:
                            if (afterThrow.ContainsKey(key))
                            {
                                //当默认的添加满的时候 自动分组
                                if (string.IsNullOrEmpty(key)) key = attribute.GetType().FullName;

                                if (afterThrow.ContainsKey(key))
                                    throw new InvalidOperationException(
                                        $"The Aspect target class `{aspectClass.CurrentType.Namespace + "." + aspectClass.CurrentType.Name}` method $`{method.Name}` can not be register multi [AspectAfterThrows]${(!string.IsNullOrEmpty(key) ? " with key:`" + key + "`" : "")}!");
                            }

                            afterThrow.Add(key, aspectAfterThrowing);
                            if (!allGroupNameList.ContainsKey(key)) allGroupNameList.Add(key, string.Empty);
                            break;
                        case AspectArround aspectPointAttribute:
                            if (arround.ContainsKey(key))
                            {
                                //当默认的添加满的时候 自动分组
                                if (string.IsNullOrEmpty(key)) key = attribute.GetType().FullName;

                                if (arround.ContainsKey(key))
                                    throw new InvalidOperationException(
                                        $"The Aspect target class `{aspectClass.CurrentType.Namespace + "." + aspectClass.CurrentType.Name}` method $`{method.Name}` can not be register multi [AspectArround]${(!string.IsNullOrEmpty(key) ? " with key:`" + key + "`" : "")}!");
                            }

                            arround.Add(key, aspectPointAttribute);
                            if (!allGroupNameList.ContainsKey(key)) allGroupNameList.Add(key, string.Empty);
                            break;
                    }
                }


                foreach (var groupName in allGroupNameList.Keys)
                {
                    var adviceMethod = new AdviceMethod();

                    adviceMethod.GroupName = groupName;

                    if (beforeCache.ContainsKey(groupName))
                    {
                        adviceMethod.AspectBefore = beforeCache[groupName];
                        //同一个groupName 设置的 OrderIndex 不一样报错
                        if (adviceMethod.OrderIndex > 0 && adviceMethod.AspectBefore.OrderIndex > 0 &&
                            adviceMethod.OrderIndex != adviceMethod.AspectBefore.OrderIndex)
                            throw new InvalidOperationException(
                                $"The Aspect target class `{aspectClass.CurrentType.Namespace + "." + aspectClass.CurrentType.Name}` method $`{method.Name}` with same groupName=`${groupName}` but OrderIndex is different !");

                        adviceMethod.OrderIndex = adviceMethod.AspectBefore.OrderIndex;
                    }

                    if (afterCache.ContainsKey(groupName))
                    {
                        adviceMethod.AspectAfter = afterCache[groupName];
                        //同一个groupName 设置的 OrderIndex 不一样报错
                        if (adviceMethod.OrderIndex > 0 && adviceMethod.AspectAfter.OrderIndex > 0 &&
                            adviceMethod.OrderIndex != adviceMethod.AspectAfter.OrderIndex)
                            throw new InvalidOperationException(
                                $"The Aspect target class `{aspectClass.CurrentType.Namespace + "." + aspectClass.CurrentType.Name}` method $`{method.Name}` with same groupName=`${groupName}` but OrderIndex is different !");

                        adviceMethod.OrderIndex = adviceMethod.AspectAfter.OrderIndex;
                    }

                    if (afterReturnCache.ContainsKey(groupName))
                    {
                        adviceMethod.AspectAfterReturn = afterReturnCache[groupName];
                        //同一个groupName 设置的 OrderIndex 不一样报错
                        if (adviceMethod.OrderIndex > 0 && adviceMethod.AspectAfterReturn.OrderIndex > 0 &&
                            adviceMethod.OrderIndex != adviceMethod.AspectAfterReturn.OrderIndex)
                            throw new InvalidOperationException(
                                $"The Aspect target class `{aspectClass.CurrentType.Namespace + "." + aspectClass.CurrentType.Name}` method $`{method.Name}` with same groupName=`${groupName}` but OrderIndex is different !");

                        adviceMethod.OrderIndex = adviceMethod.AspectAfterReturn.OrderIndex;
                    }

                    if (afterThrow.ContainsKey(groupName))
                    {
                        adviceMethod.AspectAfterThrows = afterThrow[groupName];
                        //同一个groupName 设置的 OrderIndex 不一样报错
                        if (adviceMethod.OrderIndex > 0 && adviceMethod.AspectAfterThrows.OrderIndex > 0 &&
                            adviceMethod.OrderIndex != adviceMethod.AspectAfterThrows.OrderIndex)
                            throw new InvalidOperationException(
                                $"The Aspect target class `{aspectClass.CurrentType.Namespace + "." + aspectClass.CurrentType.Name}` method $`{method.Name}` with same groupName=`${groupName}` but OrderIndex is different !");

                        adviceMethod.OrderIndex = adviceMethod.AspectAfterThrows.OrderIndex;
                    }

                    if (arround.ContainsKey(groupName))
                    {
                        adviceMethod.AspectArround = arround[groupName];
                        //同一个groupName 设置的 OrderIndex 不一样报错
                        if (adviceMethod.OrderIndex > 0 && adviceMethod.AspectArround.OrderIndex > 0 &&
                            adviceMethod.OrderIndex != adviceMethod.AspectArround.OrderIndex)
                            throw new InvalidOperationException(
                                $"The Aspect target class `{aspectClass.CurrentType.Namespace + "." + aspectClass.CurrentType.Name}` method $`{method.Name}` with same groupName=`${groupName}` but OrderIndex is different !");

                        adviceMethod.OrderIndex = adviceMethod.AspectArround.OrderIndex;
                    }

                    aspectAttributeInfo.AdviceMethod.Add(adviceMethod);
                }

                aspectAttributeInfo.AdviceMethod = aspectAttributeInfo.AdviceMethod.OrderBy(r => r.OrderIndex).ToList();

                if (aspectClass.isDynamicGeneric || isGeneric || method.IsGenericMethod)
                {
                    DynamicCacheList.TryAdd(method.GetMethodInfoUniqueName(), aspectAttributeInfo);
                    continue;
                }

                CacheList.TryAdd(method, aspectAttributeInfo);
            }

            //释放
            aspectClass.AspectAttributeCache.Clear();
        }

        /// <summary>
        ///     缓存
        /// </summary>
        internal ConcurrentDictionary<MethodInfo, AspectAttributeChainBuilder> CacheList { get; set; }

        /// <summary>
        ///     由于动态泛型的method是跟着泛型T变化的  所以需要单独缓存
        /// </summary>
        internal ConcurrentDictionary<string, AspectAttributeChainBuilder> DynamicCacheList { get; set; }

        /// <summary>
        /// </summary>
        public void Start()
        {
        }
    }
}