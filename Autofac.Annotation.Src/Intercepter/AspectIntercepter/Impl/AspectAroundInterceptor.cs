using Autofac.Annotation;
using Autofac.Annotation.Util;
using Autofac.AspectIntercepter.Advice;
using Autofac.AspectIntercepter.Pointcut;

namespace Autofac.AspectIntercepter.Impl
{
    /// <summary>
    /// 环绕返回拦截处理器
    /// </summary>
    internal class AspectAroundInterceptor : IAdvice
    {
        private readonly AspectArround _aroundAttribute;
        private readonly string _aroundAttributeMethodName;
        private readonly AspectAfterInterceptor _aspectAfter;
        private readonly AspectAfterThrowsInterceptor _aspectThrows;
        private readonly RunTimePointcutMethod<Around> _pointCutMethod;

        public AspectAroundInterceptor(AspectArround aroundAttribute, AspectAfter aspectAfter, AspectAfterThrows chainAspectAfterThrows)
        {
            _aroundAttribute = aroundAttribute;
            _aroundAttributeMethodName = aroundAttribute.GetType().FullName + ".Around()";
            if (aspectAfter != null)
            {
                _aspectAfter = new AspectAfterInterceptor(aspectAfter, true);
            }

            if (chainAspectAfterThrows != null)
            {
                _aspectThrows = new AspectAfterThrowsInterceptor(chainAspectAfterThrows, true);
            }
        }

        public AspectAroundInterceptor(RunTimePointcutMethod<Around> pointCutMethod, AspectAfterInterceptor aspectAfter,
            AspectAfterThrowsInterceptor chainAspectAfterThrows)
        {
            _pointCutMethod = pointCutMethod;
            _aspectAfter = aspectAfter;
            _aspectThrows = chainAspectAfterThrows;
        }

        public async Task OnInvocation(AspectContext aspectContext, AspectDelegate next)
        {
            Exception exception = null;
            try
            {
                if (_aroundAttribute != null)
                {
                    using (DeadLockCheck.Enable(_aroundAttributeMethodName))
                    {
                        await _aroundAttribute.OnInvocation(aspectContext, next);
                    }

                    return;
                }

                using (DeadLockCheck.Enable(_pointCutMethod.MethodInfo.GetMethodInfoUniqueName()))
                {
                    var rt = MethodInvokeHelper.InvokeInstanceMethod(
                        _pointCutMethod.Instance,
                        _pointCutMethod.MethodInfo,
                        _pointCutMethod.MethodParameters,
                        aspectContext.ComponentContext,
                        aspectContext,
                        next,
                        injectAnotation: _pointCutMethod.PointcutInjectAnotation,
                        pointCutAnnotation: _pointCutMethod.Pointcut);
                    if (typeof(Task).IsAssignableFrom(_pointCutMethod.MethodReturnType))
                    {
                        await ((Task)rt).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                if (exception == null && _aspectAfter != null)
                {
                    await _aspectAfter.OnInvocation(aspectContext, next);
                }
            }

            try
            {
                if (exception != null && _aspectAfter != null)
                {
                    await _aspectAfter.OnInvocation(aspectContext, next);
                }

                if (exception != null && _aspectThrows != null)
                {
                    await _aspectThrows.OnInvocation(aspectContext, next);
                }
            }
            finally
            {
                if (exception != null) throw exception;
            }
        }
    }
}