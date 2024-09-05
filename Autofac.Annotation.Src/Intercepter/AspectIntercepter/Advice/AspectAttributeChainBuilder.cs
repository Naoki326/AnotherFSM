using Autofac.Annotation;
using Autofac.AspectIntercepter.Impl;

namespace Autofac.AspectIntercepter.Advice
{
    internal class AdviceMethod
    {
        /// <summary>
        /// 排序
        /// </summary>
        public int OrderIndex { get; set; }

        /// <summary>
        /// 分组名称
        /// </summary>
        public string GroupName { get; set; }


        public AspectBefore AspectBefore { get; set; }
        public AspectAfter AspectAfter { get; set; }
        public AspectAfterReturn AspectAfterReturn { get; set; }
        public AspectAfterThrows AspectAfterThrows { get; set; }
        public AspectArround AspectArround { get; set; }
    }

    /// <summary>
    /// 增强方法调用链
    /// </summary>
    internal class AspectAttributeChainBuilder
    {
        public List<AdviceMethod> AdviceMethod { get; set; }

        public readonly Lazy<AspectDelegate> AspectFunc;

        public AspectAttributeChainBuilder()
        {
            AspectFunc = new Lazy<AspectDelegate>(this.BuilderMethodChain);
        }

        private AspectDelegate BuilderMethodChain()
        {
            AspectMiddlewareBuilder builder = new AspectMiddlewareBuilder();

            var aroundIndex = 0;
            foreach (var chain in AdviceMethod)
            {
                var isLast = aroundIndex == AdviceMethod.Count - 1;

                if (chain.AspectAfterReturn != null)
                {
                    var after = new AspectAfterReturnInterceptor(chain.AspectAfterReturn);
                    //After 后加进去先执行
                    builder.Use(next => async ctx => await after.OnInvocation(ctx, next));
                }

                if (chain.AspectArround != null)
                {
                    var around = new AspectAroundInterceptor(chain.AspectArround, chain.AspectAfter, chain.AspectAfterThrows);
                    //Arround 先加进去先执行 后续执行权脚在了Arround的实际运行方法
                    builder.Use(next => async ctx => { await around.OnInvocation(ctx, next); });
                }

                if (chain.AspectArround == null && chain.AspectAfterThrows != null)
                {
                    var aspectThrowingInterceptor = new AspectAfterThrowsInterceptor(chain.AspectAfterThrows);
                    builder.Use(next => async ctx => { await aspectThrowingInterceptor.OnInvocation(ctx, next); });
                }

                if (chain.AspectArround == null && chain.AspectAfter != null)
                {
                    var after = new AspectAfterInterceptor(chain.AspectAfter);
                    builder.Use(next => async ctx => await after.OnInvocation(ctx, next));
                }

                if (chain.AspectBefore != null)
                {
                    //Before先加进去先执行
                    var before = new AspectBeforeInterceptor(chain.AspectBefore);
                    builder.Use(next => async ctx => await before.OnInvocation(ctx, next));
                }

                aroundIndex++;
                if (!isLast) continue;

                //真正的方法
                builder.Use(next => async ctx =>
                {
                    try
                    {
                        await ctx.Proceed();
                    }
                    catch (Exception ex)
                    {
                        ctx.Exception = ex; // 只有这里会设置值 到错误增强器里面去处理并 在最后一个错误处理器里面throw
                        throw;
                    }

                    await next(ctx);
                });
            }

            var aspectfunc = builder.Build();
            return aspectfunc;
        }
    }
}