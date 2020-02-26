using System.Collections.Generic;
using Akka.Actor;

namespace MovieTicketingApp
{
    public class IndexDict
    {

        public Dictionary<IActorRef, int> entries { get; }

        public IndexDict()
        {
            entries = new Dictionary<IActorRef, int>();
        }

        public int getValue(IActorRef node)
        {
            int index = 1;
            this.entries.TryGetValue(node, out index);
            return index;
        }

        public void updateValue(IActorRef node, int index)
        {
            if (this.entries.ContainsKey(node)) { this.entries.Remove(node); }
            this.entries.Add(node, index);
        }

    }
}