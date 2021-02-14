using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using RabbitMQ.Client.Core.DependencyInjection.Exceptions;
using RabbitMQ.Client.Core.DependencyInjection.Models;
using RabbitMQ.Client.Core.DependencyInjection.Services.Interfaces;
using RabbitMQ.Client.Events;

namespace RabbitMQ.Client.Core.DependencyInjection.Services
{
    /// <summary>
    /// An implementation of custom RabbitMQ consuming service interface.
    /// </summary>
    public class ConsumingService : IConsumingService, IDisposable
    {
        public IConnection Connection { get; private set; }

        public IModel Channel { get; private set; }
        
        public AsyncEventingBasicConsumer Consumer { get; private set; }

        private bool _consumingStarted;

        private readonly IMessageHandlingPipelineExecutingService _messageHandlingPipelineExecutingService;
        private readonly IEnumerable<RabbitMqExchange> _exchanges;

        private IEnumerable<string> _consumerTags = new List<string>();

        public ConsumingService(
            IMessageHandlingPipelineExecutingService messageHandlingPipelineExecutingService,
            IEnumerable<RabbitMqExchange> exchanges)
        {
            _messageHandlingPipelineExecutingService = messageHandlingPipelineExecutingService;
            _exchanges = exchanges;
        }

        public void Dispose()
        {
            if (Channel?.IsOpen == true)
            {
                Channel.Close((int)HttpStatusCode.OK, "Channel closed");
            }

            if (Connection?.IsOpen == true)
            {
                Connection.Close();
            }

            Channel?.Dispose();
            Connection?.Dispose();
        }

        public void UseConnection(IConnection connection)
        {
            Connection = connection;
        }

        public void UseChannel(IModel channel)
        {
            Channel = channel;
        }

        public void UseConsumer(AsyncEventingBasicConsumer consumer)
        {
            Consumer = consumer;
        }

        public void StartConsuming()
        {
            if (Channel is null)
            {
                throw new ConsumingChannelIsNullException($"Consuming channel is null. Configure {nameof(IConsumingService)} or full functional {nameof(IConsumingService)} for consuming messages.");
            }

            if (_consumingStarted)
            {
                return;
            }

            Consumer.Received += ConsumerOnReceived;
            _consumingStarted = true;

            var consumptionExchanges = _exchanges.Where(x => x.IsConsuming);
            _consumerTags = consumptionExchanges.SelectMany(
                    exchange => exchange.Options.Queues.Select(
                        queue => Channel.BasicConsume(queue: queue.Name, autoAck: false, consumer: Consumer)))
                .Distinct()
                .ToList();
        }

        public void StopConsuming()
        {
            if (Channel is null)
            {
                throw new ConsumingChannelIsNullException($"Consuming channel is null. Configure {nameof(IConsumingService)} or full functional {nameof(IConsumingService)} for consuming messages.");
            }

            if (!_consumingStarted)
            {
                return;
            }

            Consumer.Received -= ConsumerOnReceived;
            _consumingStarted = false;
            foreach (var tag in _consumerTags)
            {
                Channel.BasicCancel(tag);
            }
        }

        // TODO: take a look at _messageHandlingPipelineExecutingService and its paradigm.
        private Task ConsumerOnReceived(object sender, BasicDeliverEventArgs eventArgs) => _messageHandlingPipelineExecutingService.Execute(eventArgs, this);
    }
}