using System;
using Akka.Actor;
using Akka.Event;
using System.Collections.Generic;

namespace MovieTicketingApp
{
    public class TicketingFrontEnd : UntypedActor
    {
        const int NUM_TICKET_RESERVED_PER_REQUEST = 10;
        const int TIMEOUT_SECONDS = 1;
        private ILoggingAdapter log = Context.GetLogger();
        protected IActorRef preferredServer;
        protected IActorRef currentServer;
        protected List<IActorRef> raftNodes;
        protected List<IActorRef> frontEndNodes;
        protected int reservedTicketCount;
        protected int pendingPurchaseQuantity;
        protected bool soldOut { get; private set; }
        protected object lastMessageSentToServer = null;
        protected bool receivedPingResponse = true;
        protected List<int> nonPreferredNodeIndexes = new List<int>();
        protected bool findingANewServer;
        protected bool timerForSwitchingBackToPreferredServerSet;
        public TicketingFrontEnd(IActorRef preferredServer, List<IActorRef> raftNodes, List<IActorRef> frontEndNodes)
        {
            this.preferredServer = preferredServer;
            this.currentServer = preferredServer;
            this.raftNodes = raftNodes;
            this.frontEndNodes = frontEndNodes;
            for (int i = 0; i < raftNodes.Count; i++)
            {
                if (raftNodes[i].Equals(preferredServer)) { continue; }
                this.nonPreferredNodeIndexes.Add(i);
            }
        }
        protected override void PreStart()
        {
            //Reserve the initial tickets
            Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromSeconds(1), this.currentServer, new ReserveTicket(NUM_TICKET_RESERVED_PER_REQUEST), Self);
            //Check whether the current server is inaccessible
            Context.System.Scheduler.ScheduleTellRepeatedly(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), this.currentServer, new Ping(), Self);
            //Timeout for not receiving any ping response
            // Context.SetReceiveTimeout(TimeSpan.FromSeconds(4));
        }
        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ReceiveTimeout timeout:
                    this.findingANewServer = true;
                    log.Info("Server timeout. Finding a new server ...");
                    this.findNewServer();
                    SetReceiveTimeout(null);
                    break;
                case ReserveTicket command:
                    this.currentServer.Tell(command);
                    break;
                case ReserveTicketResponse response:
                    this.handleReserveTicketResponse(response);
                    this.resetServerTimeout();
                    break;
                case QueryTicket command:
                    if (this.findingANewServer) { log.Info("Server is down. Cannot process the request."); return; }
                    this.currentServer.Tell(command);
                    break;
                case QueryTicketResponse response:
                    this.handleQueryTicketResponse(response);
                    this.resetServerTimeout();
                    break;
                case PurchaseTicket command:
                    if (this.findingANewServer) { log.Info("Server is down. Cannot process the request."); return; }
                    this.handlePurchaseTicket(command);
                    break;
                case PurchaseTicketResponse response:
                    this.handlePurchaseTicketResponse(response);
                    this.resetServerTimeout();
                    break;
                case ReservationShareQuery request:
                    //Respond true if we have enough ticket to share, otherwise respond false
                    Sender.Tell(new ReservationShareQueryResponse(this.reservedTicketCount >= request.quantity, request.quantity));
                    break;
                case ReservationShareQueryResponse response:
                    //If the other node has enough to share, ask it to give up
                    if (response.success)
                    {
                        Sender.Tell(new ReservationShareRequest(response.quantity));
                    }
                    else
                    {

                    }
                    break;
                case ReservationShareRequest confirmation:
                    //The requester is aware of what we can give up and confirms that it wants this to give up
                    //So give up
                    this.currentServer.Tell(new GiveUpReservedTicket(confirmation.quantity, Sender.Path.ToString()));
                    break;
                case ReservationShareRequestResponse response:
                    if (response.success)
                    {
                        //A front-end node gave up its reserved amount successfully, reserve some more from the server
                        this.currentServer.Tell(new ReserveTicket(response.quantity));
                    }
                    else
                    {
                        //A front-end node could not give up what it promised, this pending purchase cannot be completed.
                        this.failPendingPurchase(response);
                    }
                    break;
                case GiveUpReservedTicketResponse response:
                    if (!response.success)
                    {
                        if (response.leader != null)
                        {
                            response.leader.Tell(new GiveUpReservedTicket(response.quantity, response.giveUpForFrontEndPath));
                        }
                        else
                        {
                            ActorSelection actorSelection = Context.System.ActorSelection(response.giveUpForFrontEndPath);
                            actorSelection.Tell(new ReservationShareRequestResponse(false, response.quantity));
                            log.Info(string.Concat(Self.Path.Name, " failed to give up ", response.quantity, " tickets."));
                        }
                    }
                    else
                    {
                        //Gave up the reserved tickets successfully, tell the requester to contact the raft node to take the amount this front-end just gave up.
                        ActorSelection targetFrontEnd = Context.System.ActorSelection(response.giveUpForFrontEndPath);
                        targetFrontEnd.Tell(new ReservationShareRequestResponse(true, response.quantity));
                    }
                    this.resetServerTimeout();
                    break;
                case PingResponse response:
                    if (Self.Path.Name == "Client-1") { log.Info("Received ping reply from server."); }
                    this.findingANewServer = false;
                    //Reset the timeout timer
                    this.resetServerTimeout();
                    break;
                case SwitchBackToPreferredServer command:
                    this.timerForSwitchingBackToPreferredServerSet = false;
                    if (this.pendingPurchaseQuantity > 0) { this.setTimerForSwitchingBackPreferredServer(); return; }
                    log.Info("Switching back to the preferred server ....");
                    this.currentServer = this.preferredServer;
                    this.findingANewServer = true;
                    break;
            }
        }

        protected void recordLastSentRequest(object message, IActorRef sentTo)
        {
            this.lastMessageSentToServer = message;
            this.resetServerTimeout();
        }

        protected void findNewServer()
        {
            Random randomizer = new Random();
            this.currentServer = raftNodes[this.nonPreferredNodeIndexes[randomizer.Next(0, this.nonPreferredNodeIndexes.Count)]];
            Context.System.Scheduler.ScheduleTellRepeatedly(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), this.currentServer, new Ping(), Self);
            this.resetServerTimeout();
            this.findingANewServer = false;
            this.setTimerForSwitchingBackPreferredServer();
        }

        protected void setTimerForSwitchingBackPreferredServer()
        {
            // if (this.timerForSwitchingBackToPreferredServerSet) { return; }
            // //After 10 seconds, try to switch back to the preferred server
            // Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromSeconds(10), Self, new SwitchBackToPreferredServer(), Self);
            // this.timerForSwitchingBackToPreferredServerSet = true;
        }

        protected void resetServerTimeout()
        {
            // Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromSeconds(1), this.currentServer, new Ping(), Self);
            Context.SetReceiveTimeout(TimeSpan.FromMilliseconds(1500));
        }

        protected void handleReserveTicketResponse(ReserveTicketResponse response)
        {
            //Redirect the original request to the leader
            if (!response.success && response.leader != null) { response.leader.Tell(new ReserveTicket(response.originalRequestQuantity)); return; }

            //If we have a pending purchase, process it
            if (this.pendingPurchaseQuantity > 0)
            {
                this.handlePendingPurchase(response);
            }
            else
            {
                //Otherwise, just increase the reserved ticket count
                this.reservedTicketCount += response.successfullyReservedQuantity;
                log.Info(string.Concat(Self.Path.Name, " has sucessfully reserved ", response.successfullyReservedQuantity, " tickets"));
            }
        }

        protected void handleQueryTicketResponse(QueryTicketResponse response)
        {
            //Log the number of available tickets
            log.Info(string.Concat(Context.Sender.Path.Name, " says there are ", response.availableTicketCount, " available tickets"));

            //If we have a pending purchase, process it
            this.handlePendingPurchase(response);
        }

        protected void handlePurchaseTicket(PurchaseTicket purchaseTicket)
        {
            //If we are having a pending purchase, refuse the request
            if (this.pendingPurchaseQuantity > 0)
            {
                log.Info(string.Concat(Self.Path.Name, " is processing a purchase. Not available to process another purchase."));
                return;
            }

            //If the purchase quantity < the reserved count, record the sale
            if (purchaseTicket.purchaseQuantity <= this.reservedTicketCount)
            {
                this.sellTicketFromReserved(purchaseTicket.purchaseQuantity);
                //If we are out of reserved tickets, request some more
                if (this.reservedTicketCount == 0)
                {
                    this.currentServer.Tell(new ReserveTicket(NUM_TICKET_RESERVED_PER_REQUEST));
                }
            }
            else
            {
                //This is going to be a lengthy process, cache the quantity and the sender
                this.pendingPurchaseQuantity = purchaseTicket.purchaseQuantity;

                //Do a inconsistent read to see whether we still have tickets for sale
                this.currentServer.Tell(new QueryTicket());
            }
        }

        protected void handlePurchaseTicketResponse(PurchaseTicketResponse response)
        {
            //Redirect the request to the leader
            if (!response.success && response.leader != null) { response.leader.Tell(new PurchaseTicket(response.originalPurchaseQuantity)); return; }

            //Display the final result
            if (response.success)
            {
                log.Info(string.Concat(Self.Path.Name, " sucessfully reported the sale of ", response.originalPurchaseQuantity, " tickets to server."));
            }
            else
            {
                log.Info(string.Concat(Self.Path.Name, " failed to report the sale of ", response.originalPurchaseQuantity, " tickets to server."));
            }
        }

        protected void handlePendingPurchase(object message)
        {
            if (this.pendingPurchaseQuantity == 0) { return; }

            switch (message)
            {
                case QueryTicketResponse response:

                    //If the purchase quantity is greater than the available ticket count, return
                    if (this.pendingPurchaseQuantity > response.availableTicketCount)
                    {
                        log.Info(string.Concat(Self.Path.Name, " says there is not enough tickets to fulfill the pending purchase."));
                        this.failPendingPurchase(response);
                        return;
                    }

                    //If the purchase quantity is less than or equal to the current reserved + the server's total unreserved,
                    //Simply reserve more from the server and make the sales
                    if (this.pendingPurchaseQuantity <= this.reservedTicketCount + (response.availableTicketCount - response.reservedTicketLog.totalReserved))
                    {
                        //If there are available tickets to fulfill this purchase, reserve more tickets
                        this.currentServer.Tell(new ReserveTicket(this.pendingPurchaseQuantity - this.reservedTicketCount));
                        return;
                    }

                    //Ask other front-end nodes for help
                    if (this.pendingPurchaseQuantity <= response.reservedTicketLog.totalReserved)
                    {
                        int neededQuantity = this.pendingPurchaseQuantity - this.reservedTicketCount;
                        int quantityToRequest = 0;
                        foreach (KeyValuePair<string, int> record in response.reservedTicketLog.log)
                        {
                            //Don't ask yourself or node that does not have anything for help
                            if (Self.Path.ToString().Equals(record.Key) || record.Value == 0) { continue; }
                            if (neededQuantity <= 0) { break; }
                            //Ask what the actor has, and stop after asking enough 
                            ActorSelection targetFrontEnd = Context.System.ActorSelection(record.Key);
                            //Don't ask more than what we need
                            quantityToRequest = Math.Min(neededQuantity, record.Value);
                            targetFrontEnd.Tell(new ReservationShareQuery(quantityToRequest));
                            neededQuantity -= quantityToRequest;
                        }
                    }

                    break;
                case ReserveTicketResponse response:

                    this.reservedTicketCount += response.successfullyReservedQuantity;
                    if (this.reservedTicketCount >= this.pendingPurchaseQuantity)
                    {
                        this.sellTicketFromReserved(this.pendingPurchaseQuantity);
                    }

                    break;
            }

        }

        protected void sellTicketFromReserved(int soldQuantity)
        {
            this.reservedTicketCount -= soldQuantity;
            //TODO: Update the persistent storage

            //Report to the raft node
            this.currentServer.Tell(new PurchaseTicket(soldQuantity));
            //No more pending purchase
            this.pendingPurchaseQuantity = 0;
            //Debugging log
            // log.Info(string.Concat(Self.Path.Name, " sold ", soldQuantity, " tickets from its reserve."));
        }

        protected void failPendingPurchase(object message)
        {
            this.pendingPurchaseQuantity = 0;
        }

        protected void clearPendingPurchaseInfo()
        {
            this.pendingPurchaseQuantity = 0;
        }
    }
}