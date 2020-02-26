using System;
using Akka.Actor;
using Akka.Event;

namespace MovieTicketingApp
{
    public class StorageActor : UntypedActor
    {
        private ILoggingAdapter log = Context.GetLogger();

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Storage storage:
                    FileManagement.writeFile(storage);
                    break;
                case WriteState state:
                    FileManagement.writeStateMachineFile(state.nodeName, state.state);
                    break;
            }
        }
    }
}