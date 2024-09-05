using System;
using System.Collections.Generic;
using System.Reflection;

namespace StateMachine
{
    public interface IContainerWrapper
    {
        T GetInstance<T>(string key) where T : notnull;

        IEnumerable<T> GetAllInstances<T>(string key) where T : notnull;
        IEnumerable<object> GetAllKeys<T>() where T : notnull;
        IEnumerable<Type> GetAllTypes<T>() where T : notnull;

        IEnumerable<KeyValuePair<object, Type>> GetAllKeyTypePairs<T>() where T : notnull;
    }

    //一个默认的全局容器，不需要局部容器需求的对象可以存放在这里
    public static class IoC
    {
        public static IContainerWrapper ContainerWrapper { get; set; } = default!;

        public static Assembly[] Assemblies { get; set; } = default!;

        public static T Get<T>(string key = default!) where T : notnull
        {
            return ContainerWrapper.GetInstance<T>(key);
        }

        public static IEnumerable<T> GetAll<T>(string key = default!) where T : notnull
        {
            return ContainerWrapper.GetAllInstances<T>(key);
        }

        public static IEnumerable<object> GetAllKeys<T>() where T : notnull
        {
            return ContainerWrapper.GetAllKeys<T>();
        }

        public static IEnumerable<Type> GetAllTypes<T>() where T : notnull
        {
            return ContainerWrapper.GetAllTypes<T>();
        }

        public static IEnumerable<KeyValuePair<object, Type>> GetAllKeyTypePairs<T>() where T : notnull
        {
            return ContainerWrapper.GetAllKeyTypePairs<T>();
        }
    }
}
