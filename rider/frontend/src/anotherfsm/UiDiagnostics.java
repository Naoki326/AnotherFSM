package anotherfsm;

import java.util.ArrayDeque;
import java.util.List;
import java.util.UUID;

/** Bounded plain-data observation only. Never retains documents, paths, text or reply JSON. */
public final class UiDiagnostics {
  public enum Stage { RESOLVE, REFERENCES, NAVIGATE_TYPE, VALIDATE_LOCATION, HOST_CHOICE, SOURCE_CHOICE }
  public enum Cause { SUBMITTED, REPLY_ACCEPTED, COMPLETED, DOCUMENT_CHANGED, EDITOR_RELEASED,
    CONTEXT_REPLACED, CONTEXT_INVALIDATED, TRANSPORT_CHANGED, INVALIDATION_UNREADABLE,
    SUPERSEDED, TIMED_OUT, REPLY_REJECTED, REQUEST_FAILED, USER_CANCELLED, DISPOSED }
  public record Entry(long sequence,String requestId,Stage stage,Cause cause,long documentVersion,long resultEpoch,long currentEpoch) {}
  private final int capacity;
  private final ArrayDeque<Entry> records=new ArrayDeque<>();
  private long sequence;

  public UiDiagnostics(int capacity) {
    if(capacity<1||capacity>4096)throw new IllegalArgumentException("Diagnostic capacity must be within 1..4096");
    this.capacity=capacity;
  }
  public synchronized void record(String requestId,Stage stage,Cause cause,long documentVersion,long resultEpoch,long currentEpoch) {
    if(stage==null||cause==null)throw new IllegalArgumentException("Stage and cause required");
    String safe="unavailable";
    try {if(UUID.fromString(requestId).toString().equals(requestId))safe=requestId;}catch(IllegalArgumentException|NullPointerException ignored){}
    if(records.size()==capacity)records.removeFirst();
    records.addLast(new Entry(++sequence,safe,stage,cause,documentVersion,resultEpoch,currentEpoch));
  }
  public synchronized List<Entry> snapshot() {return List.copyOf(records);}
}
