namespace Wedding.Api.Data;

public enum QuestionType
{
    Text,
    SingleChoice,
    MultiChoice,
    YesNo
}

public enum Attendance
{
    NotAnswered,
    Yes,
    No
}

public class Guest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Token { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<GuestEvent> GuestEvents { get; set; } = new();
    public List<Companion> Companions { get; set; } = new();
    public List<Answer> Answers { get; set; } = new();
    public List<Submission> Submissions { get; set; } = new();
}

public class Event
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime Date { get; set; }
    public string Location { get; set; } = "";
    public string Description { get; set; } = "";
    public int Order { get; set; }

    public List<GuestEvent> GuestEvents { get; set; } = new();
    public List<Question> Questions { get; set; } = new();
}

public class GuestEvent
{
    public int GuestId { get; set; }
    public Guest Guest { get; set; } = null!;
    public int EventId { get; set; }
    public Event Event { get; set; } = null!;
    public int CompanionsAllowed { get; set; }
    public Attendance Attendance { get; set; } = Attendance.NotAnswered;
}

public class Companion
{
    public int Id { get; set; }
    public int GuestId { get; set; }
    public Guest Guest { get; set; } = null!;
    public string Name { get; set; } = "";
}

public class Question
{
    public int Id { get; set; }
    public int? EventId { get; set; }
    public Event? Event { get; set; }
    public string Text { get; set; } = "";
    public QuestionType Type { get; set; }
    public string Options { get; set; } = "";
    public bool IsPerCompanion { get; set; }
    public bool IsRequired { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Answer
{
    public int Id { get; set; }
    public int GuestId { get; set; }
    public Guest Guest { get; set; } = null!;
    public int? CompanionId { get; set; }
    public Companion? Companion { get; set; }
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public string Value { get; set; } = "";
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

public class Submission
{
    public int Id { get; set; }
    public int GuestId { get; set; }
    public Guest Guest { get; set; } = null!;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public string? CompanionName { get; set; }
    public string AnswersJson { get; set; } = "";
}

public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class Upload
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}