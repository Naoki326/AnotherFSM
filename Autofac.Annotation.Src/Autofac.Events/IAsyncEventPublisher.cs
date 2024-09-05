namespace Autofac.Events
{
    /// <summary>
    /// Publishes an event asynchronously
    /// </summary>
    public interface IAsyncEventPublisher
    {
        /// <summary>
        /// 发布消息
        /// </summary>
        /// <param name="event"></param>
        /// <returns></returns>
        Task PublishAsync(object @event);

        /// <summary>
        /// 发布消息并且拿到返回值
        /// </summary>
        /// <param name="event"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        Task<List<T>> PublishAsync<T>(object @event);
    }
}