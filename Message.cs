using System.Collections;
using System.Collections.Generic;
using Akka.Actor;

namespace MovieTicketingApp
{
    public class KillLeader { }
    public class PoisonLeader { }

    public class PrintLogEntries { }

    public class End { }

    public class WriteState
    {
        public string nodeName { get; }
        public string state { get; }
        public WriteState(string nodeName, string state)
        {
            this.nodeName = nodeName;
            this.state = state;
        }
    }

    public class SendClientRequest
    {
        public string value { get; }
        public SendClientRequest(string value)
        {
            this.value = value;
        }
    }
    public class ClientRequest
    {
        public string value { get; }
        public ClientRequest(string value)
        {
            this.value = value;
        }
    }

    public class ClientRequestResponse
    {
        public string value { get; }
        public bool success { get; }
        public IActorRef leader { get; }
        public ClientRequestResponse(string value, bool success, IActorRef leader)
        {
            this.value = value;
            this.success = success;
            this.leader = leader;
        }
    }

    public class RequestVote
    {
        public int candidateID { get; }
        public int term { get; }
        public int lastLogIndex { get; }
        public int lastLogTerm { get; }
        public RequestVote(int candidateID, int term, int lastLogIndex, int lastLogTerm)
        {
            this.candidateID = candidateID;
            this.term = term;
            this.lastLogIndex = lastLogIndex;
            this.lastLogTerm = lastLogTerm;
        }
    }

    public class RequestVoteResponse
    {
        public int term { get; }
        public bool voteGranted { get; }
        public RequestVoteResponse(int term, bool voteGranted)
        {
            this.term = term;
            this.voteGranted = voteGranted;
        }
    }

    public class AppendEntries
    {
        public int term { get; }
        public int leaderId { get; }
        public int prevLogIndex { get; }
        public int prevLogTerm { get; }
        public List<LogEntry> entries { get; }
        public int commitIndex { get; }
        public AppendEntries(int term, int leaderId, int prevLogIndex, int prevLogTerm, List<LogEntry> entries, int commitIndex)
        {
            this.term = term;
            this.leaderId = leaderId;
            this.prevLogIndex = prevLogIndex;
            this.prevLogTerm = prevLogTerm;
            this.entries = entries;
            this.commitIndex = commitIndex;
        }
    }

    public class AppendEntriesResponse
    {
        public int term { get; }
        public bool success { get; }
        public int lastLogIndex { get; }
        public AppendEntriesResponse(int term, bool success, int lastLogIndex)
        {
            this.term = term;
            this.success = success;
            this.lastLogIndex = lastLogIndex;
        }
    }

    public class GetLeaderPath { }
    public class GetStateMachineString { }


    public class UnstableReadCommand { }
    public class UnstableRead { }

    public class UnstableReadResponse
    {
        public bool emptyLog { get; }
        public bool isTentative { get; }
        public string value { get; }
        public UnstableReadResponse(string value, bool emptyLog, bool isTentative)
        {
            this.value = value;
            this.emptyLog = emptyLog;
            this.isTentative = isTentative;
        }
    }

    public class ReserveTicket
    {
        public int requestQuantity;
        public ReserveTicket(int requestQuantity)
        {
            this.requestQuantity = requestQuantity;
        }
    }
    public class ReserveTicketResponse
    {
        public bool success { get; }
        public IActorRef leader { get; }
        public int originalRequestQuantity { get; }
        public int successfullyReservedQuantity { get; }
        public ReserveTicketResponse(bool success, int successfullyReservedQuantity, int originalRequestedQuantity, IActorRef leader)
        {
            this.success = success;
            this.successfullyReservedQuantity = successfullyReservedQuantity;
            this.originalRequestQuantity = originalRequestedQuantity;
            this.leader = leader;
        }
    }

    public class GiveUpReservedTicket
    {
        public int quantity;
        public string giveUpForFrontEndPath;
        public GiveUpReservedTicket(int quantity, string giveUpForFrontEndPath)
        {
            this.quantity = quantity;
            this.giveUpForFrontEndPath = giveUpForFrontEndPath;
        }
    }

    public class GiveUpReservedTicketResponse
    {
        public bool success;
        public int quantity;
        public string giveUpForFrontEndPath;
        public IActorRef leader;
        public GiveUpReservedTicketResponse(bool success, int quantity, string giveUpForFrontEndPath, IActorRef leader)
        {
            this.success = success;
            this.quantity = quantity;
            this.giveUpForFrontEndPath = giveUpForFrontEndPath;
            this.leader = leader;
        }
    }

    public class QueryTicket { }
    public class QueryTicketResponse
    {
        public int availableTicketCount { get; }
        public ReservationLog reservedTicketLog { get; }
        public QueryTicketResponse(int availableTicketCount, ReservationLog reservedTicketLog)
        {
            this.availableTicketCount = availableTicketCount;
            this.reservedTicketLog = reservedTicketLog;
        }
    }

    public class PurchaseTicket
    {
        public int purchaseQuantity { get; }
        public PurchaseTicket(int purchaseQuantity)
        {
            this.purchaseQuantity = purchaseQuantity;
        }
    }

    public class PurchaseTicketResponse
    {
        public bool success { get; }
        public int originalPurchaseQuantity { get; }
        public IActorRef leader { get; }
        public PurchaseTicketResponse(bool success, int originalPurchaseQuantity, IActorRef leader)
        {
            this.success = success;
            this.originalPurchaseQuantity = originalPurchaseQuantity;
            this.leader = leader;
        }
    }

    public class PrintStateMachine { }

    public class ReservationShareQuery
    {
        public int quantity { get; }
        public ReservationShareQuery(int quantity)
        {
            this.quantity = quantity;
        }
    }
    public class ReservationShareQueryResponse
    {
        public int quantity { get; }
        public bool success { get; }
        public ReservationShareQueryResponse(bool success, int quantity)
        {
            this.success = success;
            this.quantity = quantity;
        }
    }
    public class ReservationShareRequest
    {
        public int quantity { get; }
        public ReservationShareRequest(int requestQuantity)
        {
            this.quantity = requestQuantity;
        }
    }
    public class ReservationShareRequestResponse
    {
        public bool success;
        public int quantity;
        public ReservationShareRequestResponse(bool success, int quantity)
        {
            this.success = success;
            this.quantity = quantity;
        }
    }

    public class Ping { }
    public class PingResponse { }
    public class SwitchBackToPreferredServer { }
}