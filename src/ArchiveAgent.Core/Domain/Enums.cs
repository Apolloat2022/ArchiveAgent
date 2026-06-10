namespace ArchiveAgent.Core.Domain;

public enum RecordStatus { Pending, Classified, Archived, NeedsReview }

public enum DataCategory { Unknown, PII, Financial, Operational, Transient }

public enum RetentionClass { Unknown, Permanent, SevenYear, OneYear, Disposable }

public enum AgentAction { Keep, Archive, Review }
