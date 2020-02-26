using System;
using Akka.Actor;
using Akka.Event;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MovieTicketingApp
{
    public class Supervisor : UntypedActor
    {
        private ILoggingAdapter log = Context.GetLogger();
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(
                ((Exception ex) => Directive.Restart),
                true // loggingEnabled
            );
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case (Props p, string n):
                    var actor = Context.ActorOf(p, n);
                    Sender.Tell(actor, Self);
                    break;
            }
        }

    }

    public class Router : UntypedActor
    {
        private ILoggingAdapter log = Context.GetLogger();
        protected List<IActorRef> frontEndNodes;
        protected List<IActorRef> raftNodes;
        protected Random randomNumberGenerator;
        protected int totalPurchase = 0;
        public Router(List<IActorRef> frontEndNodes, List<IActorRef> raftNodes)
        {
            this.frontEndNodes = frontEndNodes;
            this.raftNodes = raftNodes;
            randomNumberGenerator = new Random();
            Context.SetReceiveTimeout(TimeSpan.FromSeconds(2));
            Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromSeconds(5), this.raftNodes[0], PoisonPill.Instance, Self);
        }
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ReceiveTimeout timeout:
                    int purchaseQuantity = 3;
                    if (totalPurchase > 50) { purchaseQuantity = 2; SetReceiveTimeout(null); }
                    //Every 3 seconds, purchase a ticket from the client
                    frontEndNodes[0].Tell(new PurchaseTicket(purchaseQuantity));
                    this.totalPurchase += 3;

                    break;
            }
            // Context.SetReceiveTimeout(TimeSpan.FromSeconds(2));
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {

            //Remove all the json files
            for (int i = 0; i < 5; i++)
            {
                string nodeName = string.Concat("Node-", i + 1);
                if (!FileManagement.checkFileExists(nodeName)) { continue; }
                FileManagement.deleteFile(nodeName);
                FileManagement.deleteStateFile(nodeName);
            }

            Console.WriteLine("Starting actor...");

            var sys = ActorSystem.Create("system");

            IActorRef supervisor = sys.ActorOf(Props.Create<Supervisor>(), "super");
            IActorRef storageActor = sys.ActorOf(Props.Create<StorageActor>(), "storage");

            // Create the nodes
            IActorRef raftNode = null;
            IActorRef frontEndNode = null;
            List<IActorRef> raftNodes = new List<IActorRef>();
            List<IActorRef> frontEndNodes = new List<IActorRef>();
            int clusterSize = 5;
            for (int i = 1; i <= clusterSize; i++)
            {
                raftNode = (IActorRef)(await supervisor.Ask((Props.Create<RaftServer>(i, raftNodes, storageActor, sys), string.Concat("Node-", i))));
                raftNodes.Add(raftNode);

                frontEndNode = (IActorRef)(await supervisor.Ask((Props.Create<TicketingFrontEnd>(raftNode, raftNodes, frontEndNodes), string.Concat("Client-", i))));
                frontEndNodes.Add(frontEndNode);
            }

            // IActorRef clientActor = sys.ActorOf(Props.Create<ClientActor>(raftNodes[0], raftNodes), "client");
            IActorRef routerActor = sys.ActorOf(Props.Create<Router>(frontEndNodes, raftNodes), "router");

            bool done = false;
            while (!done)
            {

                var input = Console.ReadLine();
                switch (input)
                {
                    case "kill":
                        foreach (IActorRef server in raftNodes)
                        {
                            server.Tell(new KillLeader());
                        }
                        break;
                    case "poison":
                        foreach (IActorRef server in raftNodes)
                        {
                            server.Tell(new PoisonLeader());
                        }
                        break;
                    case "end":
                        done = true;
                        break;
                    case "read":

                        break;
                    case "":
                        foreach (IActorRef node in raftNodes)
                        {
                            //clientActor.Tell(new UnstableReadCommand(raftNode));
                            node.Tell(new PrintStateMachine());
                        }
                        break;
                        // default:
                        //     allNodes[0].Tell(new ClientRequest(input), clientActor);
                        //     break;
                }

            }

            //Remove all the json files
            foreach (IActorRef actor in raftNodes)
            {
                FileManagement.deleteFile(actor.Path.Name);
                FileManagement.deleteStateFile(actor.Path.Name);
            }

            await sys.Terminate();

        }
    }
}
