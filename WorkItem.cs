﻿using System;
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
        private readonly Func<Task> action;
        private readonly List<IMessage> sideEffects = new List<IMessage>();
        private bool started = false;

        public ProceduralWorkItem(Func<Task> action)
        {
            this.itemId = Guid.NewGuid();
            this.action = action;
        }

        public WorkItemState State { get; private set; }

        public async Task Execute()
        {
            await this.action();
            this.State = WorkItemState.Completed;
        }
    }

    public class EventDrivenWorkItem : IWorkItem
    {
        private readonly Guid itemId;
        private readonly Func<Task> action;
        private readonly List<IMessage> sideEffects = new List<IMessage>();
        private bool started = false;

        public EventDrivenWorkItem(Func<Task> action)
        {
            this.action = action;
        }

        public WorkItemState State { get; private set; }

        public IEnumerable<IMessage> ConsumeSideEffects()
        {
            var consumedSideEffects = new List<IMessage>(this.sideEffects);
            this.sideEffects.Clear();
            return consumedSideEffects;
        }
            

        public async Task Execute()
        {
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

            sideEffects.Add(new FinishSideEffect(this.itemId));
        }

        public async void Handle(FinishedEvent fact)
        {
            if (fact.Id != this.itemId)
            {
                return;
            }

            await this.action();
            this.State = WorkItemState.Completed;
        }
    }

    public class Slot
    {
        private readonly List<IWorkItem> executedItems;
        private IWorkItem? currentItem;

        public WorkItemState State { get; private set; }

        public static Slot Create() => new Slot(null, new List<IWorkItem>());

        private Slot(IWorkItem? currentItem, List<IWorkItem> executedItems)
        {
            this.currentItem = currentItem;
            this.executedItems = executedItems;
        }

        public async Task Execute(ConcurrentQueue<IWorkItem> items)
        {
            this.State = WorkItemState.Executing;

            if (currentItem is not null)
            {
                if (currentItem.State == WorkItemState.Completed)
                {
                    this.executedItems.Add(currentItem);
                }
                else
                {
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
                    return;
                }
            }

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
                .Range(0, maxParallelism)
                .Select(i => Slot.Create())
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

                var sideEffects = workItems.SelectMany(item => item.ConsumeSideEffects());
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
