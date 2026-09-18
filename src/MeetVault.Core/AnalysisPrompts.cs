using System.Text.Json;

namespace MeetVault.Core;

/// <summary>Prompt templates for the local LLM analysis and brief generation passes.</summary>
public static class AnalysisPrompts
{
    public const string ChunkSystemPrompt = """
        You are a precise meeting-analysis engine. You extract facts from meeting transcript chunks.
        Rules:
        - Only record information that is actually present in the transcript text.
        - A "decision" must be something the group actually agreed on. Proposals, questions and unresolved
          items are NOT decisions.
        - For action items, set "owner" only when a person was explicitly named. Never invent names.
        - Set "deadline" to ISO yyyy-MM-dd when an exact date is stated, otherwise short text like "next Friday".
        - "participants" lists people who clearly spoke or were addressed as attendees; only names that
          literally appear in the transcript.
        - Respond with JSON only.
        """;

    public static string ChunkUserPrompt(TranscriptChunk chunk, DateOnly meetingDate) => $"""
        Meeting date: {meetingDate:yyyy-MM-dd}. Transcript chunk {chunk.Index}
        (from {TimecodeFormatter.FormatMs(chunk.StartMs)} to {TimecodeFormatter.FormatMs(chunk.EndMs)}):
        Extract the structured meeting intelligence for this chunk.

        TRANSCRIPT:
        {chunk.Text}
        """;

    public const string FinalSystemPrompt = """
        You are a precise meeting-knowledge synthesizer. You receive compact per-chunk extractions from
        one meeting and produce the final consolidated structured analysis.
        Rules:
        - Include only facts present in the input. Do not invent names, dates or decisions.
        - meetingTitle: a short descriptive title (max ~8 words).
        - summary: 2-4 crisp sentences covering purpose and outcomes.
        - agenda: ordered list of the items the meeting set out to discuss.
        - Only real agreements go to "decisions"; speculative talk stays out.
        - relatedMeetingHints: short keywords (projects, systems, topics) useful to link other meetings.
        - Respond with JSON only.
        """;

    public static string FinalUserPrompt(ChunkExtraction merged, DateOnly meetingDate, string workingTitle) => $"""
        Meeting date: {meetingDate:yyyy-MM-dd}. Working title: {workingTitle}
        Below is the merged per-chunk extraction for the whole meeting.
        Produce the final consolidated structured analysis.

        MERGED EXTRACTION:
        {JsonUtil.ToPrettyJson(merged)}
        """;

    public const string BriefSystemPrompt = """
        You write spoken audio briefs for meetings; the text is read aloud by a TTS voice.
        Rules:
        - Use ONLY facts from the provided meeting analysis.
        - Crisp, point-to-point sentences. No greetings, no filler.
        - Structure: 1) one sentence purpose, 2) main agenda items, 3) key decisions/outcomes,
          4) action items with owner and deadline (only those with a stated owner),
          5) top open risks/questions if space remains.
        - Keep within the requested duration at normal speaking speed (about 150 words per minute).
        - Plain sentences only: no markdown, no bullets, no emojis.
        - Respond with JSON only: {"script": "..."}
        """;

    public static string BriefUserPrompt(MeetingAnalysis analysis, DateOnly date, int targetSeconds)
    {
        var words = (int)(targetSeconds * 2.5);
        var compact = new
        {
            analysis.Summary,
            analysis.Agenda,
            analysis.Decisions,
            analysis.ActionItems,
            analysis.Risks,
            analysis.OpenQuestions,
        };
        return $"""
            Meeting date: {date:yyyy-MM-dd}. Target spoken length: about {targetSeconds} seconds (max {words} words).

            MEETING ANALYSIS (JSON):
            {JsonUtil.ToPrettyJson(compact)}
            """;
    }
}
