namespace MeetVault.Core;

/// <summary>JSON schemas for llama.cpp grammar-constrained generation.</summary>
public static class AnalysisSchemas
{
    public const string Chunk = """
        {
          "type": "object",
          "properties": {
            "chunkSummary": {"type": "string"},
            "topics": {"type": "array", "items": {"type": "string"}},
            "keyDiscussionPoints": {"type": "array", "items": {"type": "string"}},
            "decisions": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "decision": {"type": "string"},
                  "sourceSegmentIds": {"type": "array", "items": {"type": "integer"}}
                },
                "required": ["decision"]
              }
            },
            "actionItems": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "task": {"type": "string"},
                  "owner": {"type": "string"},
                  "deadline": {"type": "string"},
                  "status": {"type": "string"}
                },
                "required": ["task"]
              }
            },
            "deadlines": {"type": "array", "items": {"type": "string"}},
            "risks": {"type": "array", "items": {"type": "string"}},
            "openQuestions": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "question": {"type": "string"},
                  "status": {"type": "string"}
                },
                "required": ["question"]
              }
            },
            "followUps": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "description": {"type": "string"},
                  "owner": {"type": "string"}
                },
                "required": ["description"]
              }
            },
            "participants": {"type": "array", "items": {"type": "string"}},
            "entities": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "type": {"type": "string"},
                  "name": {"type": "string"}
                },
                "required": ["name"]
              }
            }
          },
          "required": ["chunkSummary"]
        }
        """;

    public const string Final = """
        {
          "type": "object",
          "properties": {
            "meetingTitle": {"type": "string"},
            "meetingDate": {"type": "string"},
            "summary": {"type": "string"},
            "agenda": {"type": "array", "items": {"type": "string"}},
            "topics": {"type": "array", "items": {"type": "string"}},
            "keyDiscussionPoints": {"type": "array", "items": {"type": "string"}},
            "decisions": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "decision": {"type": "string"},
                  "sourceSegmentIds": {"type": "array", "items": {"type": "integer"}}
                },
                "required": ["decision"]
              }
            },
            "actionItems": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "task": {"type": "string"},
                  "owner": {"type": "string"},
                  "deadline": {"type": "string"},
                  "status": {"type": "string"}
                },
                "required": ["task"]
              }
            },
            "deadlines": {"type": "array", "items": {"type": "string"}},
            "risks": {"type": "array", "items": {"type": "string"}},
            "openQuestions": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "question": {"type": "string"},
                  "status": {"type": "string"}
                },
                "required": ["question"]
              }
            },
            "followUps": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "description": {"type": "string"},
                  "owner": {"type": "string"}
                },
                "required": ["description"]
              }
            },
            "participants": {"type": "array", "items": {"type": "string"}},
            "entities": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "type": {"type": "string"},
                  "name": {"type": "string"}
                },
                "required": ["name"]
              }
            },
            "relatedMeetingHints": {"type": "array", "items": {"type": "string"}}
          },
          "required": ["meetingTitle", "summary", "agenda", "topics", "decisions", "actionItems", "risks", "openQuestions", "followUps"]
        }
        """;

    public const string Brief = """
        {
          "type": "object",
          "properties": {
            "script": {"type": "string"}
          },
          "required": ["script"]
        }
        """;
}
