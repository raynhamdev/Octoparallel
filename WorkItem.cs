using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Octoparallel
{
    public interface IMessage
    {
        Guid Id { get; }
    }

    public class StartSideEffect : IMessage
    {
        public StartSideEffect(Guid id) => this.Id = id;

        public Guid Id { get; }
    }

    public class StartedEvent : IMessage
    {
        public StartedEvent(Guid id) => this.Id = id;

        public Guid Id { get; }
    }

    public class FinishSideEffect : IMessage
    {
        public FinishSideEffect(Guid id) => this.Id = id;

        public Guid Id { get; }
    }

    public class FinishedEvent : IMessage
    {
        public FinishedEvent(Guid id) => this.Id = id;

        public Guid Id { get; }
    }

    public interface IWorkItem
    {
        public WorkItemState State { get; }

        public Task Execute();
    }

    public enum WorkItemState
    {
        Unexecuted = 0,
        Executing,
        Completed
    }

    public class ProceduralWorkItem : IWorkItem
    {
        private readonly Guid itemId;
        private readonly string label;
        private readonly Func<Task> action;
        private readonly List<IMessage> sideEffects = new List<IMessage>();

        public static ProceduralWorkItem Create(string label, Func<Task> action)
            => new ProceduralWorkItem(label, action);

        public ProceduralWorkItem(string label, Func<Task> action)
        {
            this.itemId = Guid.NewGuid();
            this.label = label;
            this.action = action;
        }

        public WorkItemState State { get; private set; }

        public async Task Execute()
        {
            Console.WriteLine($"Executing work item {label}.");
            await this.action();
            this.State = WorkItemState.Completed;
        }
    }

    public class EventDrivenWorkItem : IWorkItem
    {
        private readonly Guid itemId;
        private readonly string label;
        private readonly Func<Task> action;
        private readonly List<IMessage> sideEffects = new List<IMessage>();
        private bool started = false;

        public EventDrivenWorkItem(Guid id, string label, Func<Task> action)
        {
            this.itemId = id;
            this.label = label;
            this.action = action;
        }

        public static EventDrivenWorkItem Create(string text) =>
            new EventDrivenWorkItem(
                Guid.NewGuid(),
                text,
                async () => {
                    await Task.CompletedTask;
                    Console.WriteLine(text);
                });

        public WorkItemState State { get; private set; }

        public IEnumerable<IMessage> ConsumeSideEffects()
        {
            var consumedSideEffects = new List<IMessage>(this.sideEffects);
            this.sideEffects.Clear();
            return consumedSideEffects;
        }
            

        public async Task Execute()
        {
            Console.WriteLine($"Executing work item {label}.");
            await Task.CompletedTask;
            this.sideEffects.Add(new StartSideEffect(this.itemId));
            this.State = WorkItemState.Executing;
        }

        public void Handle(StartedEvent fact)
        {
            if (fact.Id != this.itemId)
            {
                return;
            }

            Console.WriteLine($"Handling started event for work item {label}.");
            sideEffects.Add(new FinishSideEffect(this.itemId));
        }

        public async void Handle(FinishedEvent fact)
        {
            if (fact.Id != this.itemId)
            {
                return;
            }

            Console.WriteLine($"Handling finished event for work item {label}.");
            await this.action();
            this.State = WorkItemState.Completed;
        }
    }

    public class Slot
    {
        private readonly int slotNumber;
        private readonly List<IWorkItem> executedItems;
        private IWorkItem? currentItem;

        public WorkItemState State { get; private set; }

        public static Slot Create(int index) => new Slot(index, null, new List<IWorkItem>());

        private Slot(int slotNumber, IWorkItem? currentItem, List<IWorkItem> executedItems)
        {
            this.slotNumber = slotNumber;
            this.currentItem = currentItem;
            this.executedItems = executedItems;
        }

        public async Task Execute(ConcurrentQueue<IWorkItem> items)
        {
            Console.WriteLine($"Executing slot {slotNumber} (start)");
            this.State = WorkItemState.Executing;

            if (currentItem is not null)
            {
                if (currentItem.State == WorkItemState.Completed)
                {
                    this.executedItems.Add(currentItem);
                }
                else
                {
                    Console.WriteLine($"Executing slot {slotNumber} (finish - incomplete)");
                    return;
                }
            }

            while (items.TryDequeue(out currentItem))
            {
                await currentItem.Execute();
                if (currentItem.State == WorkItemState.Completed)
                {
                    this.executedItems.Add(currentItem);
                }
                else
                {
                    Console.WriteLine($"Executing slot {slotNumber} (finish - incomplete)");
                    return;
                }
            }

            Console.WriteLine($"Executing slot {slotNumber} (finish - complete)");
            this.State = WorkItemState.Completed;
        }
    }

    public class ParallelForeach : IWorkItem
    {
        private readonly List<Slot> slots;
        private readonly ConcurrentQueue<IWorkItem> items;

        public WorkItemState State { get; private set; }

        public static ParallelForeach Create(int maxParallelism, IEnumerable<IWorkItem> items)
        {
            var queue = new ConcurrentQueue<IWorkItem>();
            foreach (var item in items)
            {
                queue.Enqueue(item);
            }

            var slots = Enumerable
                .Range(1, maxParallelism)
                .Select(i => Slot.Create(i))
                .ToList();

            return new ParallelForeach(slots, queue);
        }

        private ParallelForeach(List<Slot> slots, ConcurrentQueue<IWorkItem> items)
        {
            this.slots = slots;
            this.items = items;
        }

        public async Task Execute()
        {
            Console.WriteLine("Executing ParallelForeach");
            await Task.WhenAll(slots.Select(s => s.Execute(items)));
            this.State = slots.Any(s => s.State == WorkItemState.Executing)
                ? WorkItemState.Executing
                : WorkItemState.Completed;
        }
    }

    public class EventLoop
    {
        private readonly Integrator integrator;

        public EventLoop(Integrator integrator)
        {
            this.integrator = integrator;
        }

        public async Task Start(List<EventDrivenWorkItem> workItems)
        {
            var parallelForeach = ParallelForeach.Create(3, workItems);

            while (parallelForeach.State != WorkItemState.Completed)
            {
                await parallelForeach.Execute();

                var sideEffects = workItems.SelectMany(item => item.ConsumeSideEffects()).ToList();

                Console.WriteLine($"Consuming {sideEffects.Count()} side-effects.");
                var events = sideEffects.Select(s => this.integrator.HandleSideEffect(s));
                // TODO: dispatch events of any type.
                foreach (var fact in events.OfType<StartedEvent>())
                {
                    foreach (var workItem in workItems)
                    {
                        workItem.Handle(fact);
                    }
                }
                foreach (var fact in events.OfType<FinishedEvent>())
                {
                    foreach (var workItem in workItems)
                    {
                        workItem.Handle(fact);
                    }
                }
            }
        }
    }

    public class Integrator
    {
        public IMessage HandleSideEffect(IMessage message)
        {
            switch (message)
            {
                case StartSideEffect start:
                    {
                        return new StartedEvent(start.Id);
                    }
                case FinishSideEffect finish:
                    {
                        return new FinishedEvent(finish.Id);
                    }
                case var _:
                    {
                        throw new NotImplementedException();
                    }
            }
        }
    }

}
