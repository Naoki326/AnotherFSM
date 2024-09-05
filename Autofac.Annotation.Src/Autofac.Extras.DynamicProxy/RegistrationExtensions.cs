// This software is part of the Autofac IoC container
// Copyright © 2013 Autofac Contributors
// https://autofac.org
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.

using Autofac.AspectIntercepter.Advice;
using Autofac.AspectIntercepter.Pointcut;
using Autofac.Builder;
using Autofac.Core;
using Autofac.Core.Resolving.Pipeline;
using Autofac.Features.Scanning;
using Castle.DynamicProxy;
using System.Reflection;


namespace Autofac.Annotation
{
    /// <summary>
    /// Adds registration syntax to the <see cref="ContainerBuilder"/> type.
    /// </summary>
    public static class RegistrationExtensions
    {
        private const string InterceptorsPropertyName = "Autofac.Extras.DynamicProxy.RegistrationExtensions.InterceptorsPropertyName";
        private const string InterceptorsForGenericMethodCache = "Autofac.Extras.DynamicProxy.RegistrationExtensions.InterceptorsForGenericMethodCache";

        private const string AttributeInterceptorsPropertyName = "Autofac.Extras.DynamicProxy.RegistrationExtensions.AttributeInterceptorsPropertyName";

        private static readonly IEnumerable<Service> EmptyServices = Enumerable.Empty<Service>();

        private static readonly ProxyGenerator ProxyGenerator = new ProxyGenerator();


        /// <summary>
        /// Enable class interception on the target type. Interceptors will be determined
        /// via Intercept attributes on the class or added with InterceptedBy().
        /// Only virtual methods can be intercepted this way.
        /// </summary>
        /// <typeparam name="TLimit">Registration limit type.</typeparam>
        /// <typeparam name="TConcreteReflectionActivatorData">Activator data type.</typeparam>
        /// <typeparam name="TRegistrationStyle">Registration style.</typeparam>
        /// <param name="registration">Registration to apply interception to.</param>
        /// <returns>Registration builder allowing the registration to be configured.</returns>
        public static IRegistrationBuilder<TLimit, TConcreteReflectionActivatorData, TRegistrationStyle> EnableClassInterceptors<TLimit,
            TConcreteReflectionActivatorData, TRegistrationStyle>(
            this IRegistrationBuilder<TLimit, TConcreteReflectionActivatorData, TRegistrationStyle> registration)
            where TConcreteReflectionActivatorData : ReflectionActivatorData
        {
            return EnableClassInterceptors(registration, ProxyGenerationOptions.Default);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TLimit"></typeparam>
        /// <typeparam name="TConcreteReflectionActivatorData"></typeparam>
        /// <typeparam name="TRegistrationStyle"></typeparam>
        /// <param name="registration"></param>
        /// <param name="additionalInterfaces"></param>
        /// <returns></returns>
        public static IRegistrationBuilder<TLimit, TConcreteReflectionActivatorData, TRegistrationStyle> EnableClassInterceptors<TLimit,
            TConcreteReflectionActivatorData, TRegistrationStyle>(
            this IRegistrationBuilder<TLimit, TConcreteReflectionActivatorData, TRegistrationStyle> registration, params Type[] additionalInterfaces)
            where TConcreteReflectionActivatorData : ReflectionActivatorData
        {
            return EnableClassInterceptors(registration, ProxyGenerationOptions.Default, additionalInterfaces);
        }

        /// <summary>
        /// Enable class interception on the target type. Interceptors will be determined
        /// via Intercept attributes on the class or added with InterceptedBy().
        /// Only virtual methods can be intercepted this way.
        /// </summary>
        /// <typeparam name="TLimit">Registration limit type.</typeparam>
        /// <typeparam name="TRegistrationStyle">Registration style.</typeparam>
        /// <param name="registration">Registration to apply interception to.</param>
        /// <param name="options">Proxy generation options to apply.</param>
        /// <param name="additionalInterfaces">Additional interface types. Calls to their members will be proxied as well.</param>
        /// <returns>Registration builder allowing the registration to be configured.</returns>
        public static IRegistrationBuilder<TLimit, ScanningActivatorData, TRegistrationStyle> EnableClassInterceptors<TLimit, TRegistrationStyle>(
            this IRegistrationBuilder<TLimit, ScanningActivatorData, TRegistrationStyle> registration,
            ProxyGenerationOptions options,
            params Type[] additionalInterfaces)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            registration.ActivatorData.ConfigurationActions.Add((t, rb) => rb.EnableClassInterceptors(options, additionalInterfaces));
            return registration;
        }

        /// <summary>
        /// Enable class interception on the target type. Interceptors will be determined
        /// via Intercept attributes on the class or added with InterceptedBy().
        /// Only virtual methods can be intercepted this way.
        /// </summary>
        /// <typeparam name="TLimit">Registration limit type.</typeparam>
        /// <typeparam name="TConcreteReflectionActivatorData">Activator data type.</typeparam>
        /// <typeparam name="TRegistrationStyle">Registration style.</typeparam>
        /// <param name="registration">Registration to apply interception to.</param>
        /// <param name="options">Proxy generation options to apply.</param>
        /// <param name="additionalInterfaces">Additional interface types. Calls to their members will be proxied as well.</param>
        /// <returns>Registration builder allowing the registration to be configured.</returns>
        public static IRegistrationBuilder<TLimit, TConcreteReflectionActivatorData, TRegistrationStyle> EnableClassInterceptors<TLimit,
            TConcreteReflectionActivatorData, TRegistrationStyle>(
            this IRegistrationBuilder<TLimit, TConcreteReflectionActivatorData, TRegistrationStyle> registration,
            ProxyGenerationOptions options,
            params Type[] additionalInterfaces)
            where TConcreteReflectionActivatorData : ReflectionActivatorData
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }


            if (registration.ActivatorData.ImplementationType.IsGenericTypeDefinition)
            {
                registration.ConfigurePipeline(p => p.Use(PipelinePhase.Activation, MiddlewareInsertionMode.StartOfPhase, (ctxt, next) =>
                {
                    next(ctxt);

                    var interceptors = GetInterceptorServices(ctxt.Registration, ctxt.Instance.GetType())
                        .Select(ctxt.ResolveService)
                        .Cast<IInterceptor>()
                        .ToArray();

                    var additionalInterface = ctxt.Instance.GetType()
                        .GetInterfaces()
                        .Where(ProxyUtil.IsAccessible)
                        .ToArray();

                    //这里需要改一下
                    //https://github.com/JSkimming/Castle.Core.AsyncInterceptor/blob/master/src/Castle.Core.AsyncInterceptor/ProxyGeneratorExtensions.cs
                    ctxt.Instance = options == null
                        ? ProxyGenerator.CreateClassProxyWithTarget(ctxt.Instance.GetType(), additionalInterface, ctxt.Instance, interceptors)
                        : ProxyGenerator.CreateClassProxyWithTarget(ctxt.Instance.GetType(), additionalInterface, ctxt.Instance, options, interceptors);
                }));
                return registration;
            }


            if (!additionalInterfaces.Any())
            {
                additionalInterfaces = registration.ActivatorData.ImplementationType
                    .GetInterfaces()
                    .Where(ProxyUtil.IsAccessible)
                    .ToArray();
            }

            registration.ActivatorData.ImplementationType =
                ProxyGenerator.ProxyBuilder.CreateClassProxyType(
                    registration.ActivatorData.ImplementationType,
                    additionalInterfaces,
                    options);

            var interceptorServices = GetInterceptorServicesFromAttributes(registration.ActivatorData.ImplementationType);
            AddInterceptorServicesToMetadata(registration, interceptorServices, AttributeInterceptorsPropertyName);

            registration.OnPreparing(e =>
            {
                var proxyParameters = new List<Parameter>();
                int index = 0;

                if (options.HasMixins)
                {
                    foreach (var mixin in options.MixinData.Mixins)
                    {
                        proxyParameters.Add(new PositionalParameter(index++, mixin));
                    }
                }

                proxyParameters.Add(new PositionalParameter(index++, GetInterceptorServices(e.Component, registration.ActivatorData.ImplementationType)
                    .Select(s => e.Context.ResolveService(s))
                    .Cast<IInterceptor>()
                    .ToArray()));

                if (options.Selector != null)
                {
                    proxyParameters.Add(new PositionalParameter(index, options.Selector));
                }

                e.Parameters = proxyParameters.Concat(e.Parameters).ToArray();
            });

            return registration;
        }

        /// <summary>
        /// 针对泛型的
        /// </summary>
        /// <param name="registration"></param>
        /// <param name="component"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        internal static IComponentRegistration EnableClassInterceptors(this IComponentRegistration registration, ComponentModel component,
            ProxyGenerationOptions options)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }


            registration.ConfigurePipeline(p => p.Use(PipelinePhase.Activation, MiddlewareInsertionMode.StartOfPhase, (ctxt, next) =>
            {
                next(ctxt);

                if (!ctxt.Registration.Metadata.TryGetValue(InterceptorsForGenericMethodCache, out var _))
                {
                    ctxt.Registration.Metadata.Add(InterceptorsForGenericMethodCache, true);
                    ctxt.Resolve<ApsectAdviceMethodInvokeCache>().AddCache(component);
                    ctxt.Resolve<PointcutMethodInvokeCache>().AddCache(component);
                }

                var interceptors = GetInterceptorServices(ctxt.Registration, ctxt.Instance.GetType())
                    .Select(ctxt.ResolveService)
                    .Cast<IInterceptor>()
                    .ToArray();

                var proxiedInterfaces = ctxt.Instance
                    .GetType()
                    .GetInterfaces()
                    .Where(ProxyUtil.IsAccessible)
                    .ToArray();


                //这里需要改一下
                ctxt.Instance = options == null
                    ? ProxyGenerator.CreateClassProxyWithTarget(ctxt.Instance.GetType(), proxiedInterfaces, ctxt.Instance, interceptors)
                    : ProxyGenerator.CreateClassProxyWithTarget(ctxt.Instance.GetType(), proxiedInterfaces, ctxt.Instance, options, interceptors);
            }));
            return registration;
        }

        /// <summary>
        /// 针对泛型的
        /// </summary>
        /// <param name="registration"></param>
        /// <param name="component"></param>
        /// <returns></returns>
        internal static IComponentRegistration EnableClassInterceptors(this IComponentRegistration registration, ComponentModel component)
        {
            return registration.EnableClassInterceptors(component, ProxyGenerationOptions.Default);
        }


        /// <summary>
        /// Enable interface interception on the target type. Interceptors will be determined
        /// via Intercept attributes on the class or interface, or added with InterceptedBy() calls.
        /// </summary>
        /// <typeparam name="TLimit">Registration limit type.</typeparam>
        /// <typeparam name="TActivatorData">Activator data type.</typeparam>
        /// <typeparam name="TSingleRegistrationStyle">Registration style.</typeparam>
        /// <param name="registration">Registration to apply interception to.</param>
        /// <returns>Registration builder allowing the registration to be configured.</returns>
        public static IRegistrationBuilder<TLimit, TActivatorData, TSingleRegistrationStyle> EnableInterfaceInterceptors<TLimit, TActivatorData,
            TSingleRegistrationStyle>(
            this IRegistrationBuilder<TLimit, TActivatorData, TSingleRegistrationStyle> registration)
        {
            return EnableInterfaceInterceptors(registration, null);
        }

        /// <summary>
        /// Enable interface interception on the target type. Interceptors will be determined
        /// via Intercept attributes on the class or interface, or added with InterceptedBy() calls.
        /// </summary>
        /// <typeparam name="TLimit">Registration limit type.</typeparam>
        /// <typeparam name="TActivatorData">Activator data type.</typeparam>
        /// <typeparam name="TSingleRegistrationStyle">Registration style.</typeparam>
        /// <param name="registration">Registration to apply interception to.</param>
        /// <param name="options">Proxy generation options to apply.</param>
        /// <returns>Registration builder allowing the registration to be configured.</returns>
        public static IRegistrationBuilder<TLimit, TActivatorData, TSingleRegistrationStyle> EnableInterfaceInterceptors<TLimit, TActivatorData,
            TSingleRegistrationStyle>(
            this IRegistrationBuilder<TLimit, TActivatorData, TSingleRegistrationStyle> registration, ProxyGenerationOptions options)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            registration.ConfigurePipeline(p => p.Use(PipelinePhase.Activation, MiddlewareInsertionMode.StartOfPhase, (ctxt, next) =>
            {
                next(ctxt);

                var proxiedInterfaces = ctxt.Instance
                    .GetType()
                    .GetInterfaces()
                    .Where(ProxyUtil.IsAccessible)
                    .ToArray();

                if (!proxiedInterfaces.Any())
                {
                    return;
                }

                var theInterface = proxiedInterfaces.First();
                var interfaces = proxiedInterfaces.Skip(1).ToArray();

                var interceptors = GetInterceptorServices(ctxt.Registration, ctxt.Instance.GetType())
                    .Select(ctxt.ResolveService)
                    .Cast<IInterceptor>()
                    .ToArray();

                //这里需要改一下
                //https://github.com/JSkimming/Castle.Core.AsyncInterceptor/blob/master/src/Castle.Core.AsyncInterceptor/ProxyGeneratorExtensions.cs
                ctxt.Instance = options == null
                    ? ProxyGenerator.CreateInterfaceProxyWithTarget(theInterface, interfaces, ctxt.Instance, interceptors)
                    : ProxyGenerator.CreateInterfaceProxyWithTarget(theInterface, interfaces, ctxt.Instance, options, interceptors);
            }));

            return registration;
        }

        /// <summary>
        /// 给泛型用的
        /// </summary>
        /// <param name="registration"></param>
        /// <param name="component"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        internal static IComponentRegistration EnableInterfaceInterceptors(
            this IComponentRegistration registration, ComponentModel component, ProxyGenerationOptions options)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            registration.ConfigurePipeline(p => p.Use(PipelinePhase.Activation, MiddlewareInsertionMode.StartOfPhase, (ctxt, next) =>
            {
                next(ctxt);

                if (!ctxt.Registration.Metadata.TryGetValue(InterceptorsForGenericMethodCache, out var _))
                {
                    ctxt.Registration.Metadata.Add(InterceptorsForGenericMethodCache, true);
                    ctxt.Resolve<ApsectAdviceMethodInvokeCache>().AddCache(component);
                    ctxt.Resolve<PointcutMethodInvokeCache>().AddCache(component);
                }

                var proxiedInterfaces = ctxt.Instance
                    .GetType()
                    .GetInterfaces()
                    .Where(ProxyUtil.IsAccessible)
                    .ToArray();

                if (!proxiedInterfaces.Any())
                {
                    return;
                }

                var theInterface = proxiedInterfaces.First();
                var interfaces = proxiedInterfaces.Skip(1).ToArray();

                var interceptors = GetInterceptorServices(ctxt.Registration, ctxt.Instance.GetType())
                    .Select(ctxt.ResolveService)
                    .Cast<IInterceptor>()
                    .ToArray();

                //这里需要改一下
                //https://github.com/JSkimming/Castle.Core.AsyncInterceptor/blob/master/src/Castle.Core.AsyncInterceptor/ProxyGeneratorExtensions.cs
                ctxt.Instance = options == null
                    ? ProxyGenerator.CreateInterfaceProxyWithTarget(theInterface, interfaces, ctxt.Instance, interceptors)
                    : ProxyGenerator.CreateInterfaceProxyWithTarget(theInterface, interfaces, ctxt.Instance, options, interceptors);
            }));

            return registration;
        }

        /// <summary>
        /// 给泛型用的
        /// </summary>
        /// <param name="registration"></param>
        /// <param name="component"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        internal static IComponentRegistration EnableInterfaceInterceptors(
            this IComponentRegistration registration, ComponentModel component)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }


            return registration.EnableInterfaceInterceptors(component, null);
        }

        /// <summary>
        /// Allows a list of interceptor services to be assigned to the registration.
        /// </summary>
        /// <typeparam name="TLimit">Registration limit type.</typeparam>
        /// <typeparam name="TActivatorData">Activator data type.</typeparam>
        /// <typeparam name="TStyle">Registration style.</typeparam>
        /// <param name="builder">Registration to apply interception to.</param>
        /// <param name="interceptorServices">The interceptor services.</param>
        /// <returns>Registration builder allowing the registration to be configured.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="builder"/> or <paramref name="interceptorServices"/>.</exception>
        public static IRegistrationBuilder<TLimit, TActivatorData, TStyle> InterceptedBy<TLimit, TActivatorData, TStyle>(
            this IRegistrationBuilder<TLimit, TActivatorData, TStyle> builder,
            params Service[] interceptorServices)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (interceptorServices == null || interceptorServices.Any(s => s == null))
            {
                throw new ArgumentNullException(nameof(interceptorServices));
            }

            AddInterceptorServicesToMetadata(builder, interceptorServices, InterceptorsPropertyName);

            return builder;
        }


        /// <summary>
        /// Allows a list of interceptor services to be assigned to the registration.
        /// </summary>
        /// <typeparam name="TLimit">Registration limit type.</typeparam>
        /// <typeparam name="TActivatorData">Activator data type.</typeparam>
        /// <typeparam name="TStyle">Registration style.</typeparam>
        /// <param name="builder">Registration to apply interception to.</param>
        /// <param name="interceptorServiceTypes">The types of the interceptor services.</param>
        /// <returns>Registration builder allowing the registration to be configured.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="interceptorServiceTypes"/>.</exception>
        public static IRegistrationBuilder<TLimit, TActivatorData, TStyle> InterceptedBy<TLimit, TActivatorData, TStyle>(
            this IRegistrationBuilder<TLimit, TActivatorData, TStyle> builder,
            params Type[] interceptorServiceTypes)
        {
            if (interceptorServiceTypes == null || interceptorServiceTypes.Any(t => t == null))
            {
                throw new ArgumentNullException(nameof(interceptorServiceTypes));
            }

            return InterceptedBy(builder, interceptorServiceTypes.Select(t => new TypedService(t)).ToArray());
        }


        private static void AddInterceptorServicesToMetadata<TLimit, TActivatorData, TStyle>(
            IRegistrationBuilder<TLimit, TActivatorData, TStyle> builder,
            IEnumerable<Service> interceptorServices,
            string metadataKey)
        {
            object existing;
            if (builder.RegistrationData.Metadata.TryGetValue(metadataKey, out existing))
            {
                builder.RegistrationData.Metadata[metadataKey] =
                    ((IEnumerable<Service>)existing).Concat(interceptorServices).Distinct();
            }
            else
            {
                builder.RegistrationData.Metadata.Add(metadataKey, interceptorServices);
            }
        }

        /// <summary>
        /// 添加拦截器类型到当前的metadata
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="interceptorType"></param>
        public static IComponentRegistration InterceptedBy(this IComponentRegistration builder, Type interceptorType)
        {
            IEnumerable<Service> services = new Service[] { new TypedService(interceptorType) };
            if (builder.Metadata.TryGetValue(InterceptorsPropertyName, out var existing))
            {
                builder.Metadata[InterceptorsPropertyName] = ((IEnumerable<Service>)existing).Concat(services).Distinct();
                ;
            }
            else
            {
                builder.Metadata.Add(InterceptorsPropertyName, services);
            }

            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static IComponentRegistration WithMetadata(this IComponentRegistration builder, string key, object value)
        {
            builder.Metadata.Add(key, value);
            return builder;
        }

        private static IEnumerable<Service> GetInterceptorServices(IComponentRegistration registration, Type implType)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            if (implType == null)
            {
                throw new ArgumentNullException(nameof(implType));
            }

            var result = EmptyServices;

            object services;
            if (registration.Metadata.TryGetValue(InterceptorsPropertyName, out services))
            {
                result = result.Concat((IEnumerable<Service>)services);
            }

            return registration.Metadata.TryGetValue(AttributeInterceptorsPropertyName, out services)
                ? result.Concat((IEnumerable<Service>)services)
                : result.Concat(GetInterceptorServicesFromAttributes(implType));
        }

        private static IEnumerable<Service> GetInterceptorServicesFromAttributes(Type implType)
        {
            var implTypeInfo = implType.GetTypeInfo();
            if (!implTypeInfo.IsClass) return Enumerable.Empty<Service>();

            var classAttributeServices = implTypeInfo
                .GetCustomAttributes(typeof(InterceptAttribute), true)
                .Cast<InterceptAttribute>()
                .Select(att => att.InterceptorService);

            var interfaceAttributeServices = implType
                .GetInterfaces()
                .SelectMany(i => i.GetTypeInfo().GetCustomAttributes(typeof(InterceptAttribute), true))
                .Cast<InterceptAttribute>()
                .Select(att => att.InterceptorService);

            return classAttributeServices.Concat(interfaceAttributeServices);
        }
    }
}