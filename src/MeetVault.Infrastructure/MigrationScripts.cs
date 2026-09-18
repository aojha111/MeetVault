namespace MeetVault.Infrastructure;

/// <summary>Raw migration SQL, split out for readability.</summary>
internal static class MigrationScripts
{
    public static readonly (string Name, string Sql)[] Scripts =
    [
        ("001_initial_schema", """
            CREATE TABLE meetings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                meeting_date TEXT NOT NULL,
                start_time TEXT,
                duration_seconds INTEGER NOT NULL DEFAULT 0,
                source_file_path TEXT,
                audio_file_path TEXT,
                transcript_file_path TEXT,
                analysis_file_path TEXT,
                audio_brief_path TEXT,
                status INTEGER NOT NULL DEFAULT 0,
                completed_stage INTEGER NOT NULL DEFAULT 0,
                last_error TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX idx_meetings_date ON meetings(meeting_date);
            CREATE INDEX idx_meetings_status ON meetings(status);

            CREATE TABLE transcript_segments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                meeting_id INTEGER NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
                sequence INTEGER NOT NULL,
                start_ms INTEGER NOT NULL,
                end_ms INTEGER NOT NULL,
                speaker TEXT,
                text TEXT NOT NULL,
                confidence REAL
            );
            CREATE INDEX idx_segments_meeting ON transcript_segments(meeting_id, sequence);

            CREATE VIRTUAL TABLE transcript_fts USING fts5(meeting_id UNINDEXED, text);

            CREATE TABLE topics (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                meeting_id INTEGER NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                description TEXT
            );
            CREATE INDEX idx_topics_meeting ON topics(meeting_id);

            CREATE TABLE decisions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                meeting_id INTEGER NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
                decision_text TEXT NOT NULL,
                source_segment_ids TEXT,
                created_at TEXT NOT NULL
            );
            CREATE INDEX idx_decisions_meeting ON decisions(meeting_id);

            CREATE TABLE action_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                meeting_id INTEGER NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
                task TEXT NOT NULL,
                owner TEXT,
                deadline TEXT,
                status TEXT NOT NULL DEFAULT 'Open',
                source_segment_ids TEXT
            );
            CREATE INDEX idx_actions_meeting ON action_items(meeting_id);
            CREATE INDEX idx_actions_status ON action_items(status);

            CREATE TABLE open_questions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                meeting_id INTEGER NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
                question TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Open'
            );
            CREATE INDEX idx_questions_meeting ON open_questions(meeting_id);

            CREATE TABLE meeting_entities (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                meeting_id INTEGER NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
                entity_type TEXT NOT NULL,
                entity_name TEXT NOT NULL
            );
            CREATE INDEX idx_entities_meeting ON meeting_entities(meeting_id);
            CREATE INDEX idx_entities_name ON meeting_entities(entity_name);

            CREATE TABLE meeting_relations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                source_meeting_id INTEGER NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
                target_meeting_id INTEGER NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
                relation_type TEXT NOT NULL,
                evidence TEXT
            );
            CREATE INDEX idx_relations_source ON meeting_relations(source_meeting_id);

            CREATE VIRTUAL TABLE knowledge_fts USING fts5(meeting_id UNINDEXED, kind, content);
            """),
        // Migration 002: rebuild the FTS tables as regular (non-contentless) FTS5 tables.
        // The original contentless tables made snippet() and unindexed-column retrieval
        // return nothing, silently breaking search. Triggers below keep pre-existing
        // contentless tables consistent as a fallback; on fresh databases the tables are
        // populated directly and the triggers simply do not fire.
        ("002_fix_fts_tables", """
            DROP TABLE IF EXISTS transcript_fts;
            DROP TABLE IF EXISTS knowledge_fts;
            CREATE VIRTUAL TABLE transcript_fts USING fts5(meeting_id UNINDEXED, text);
            CREATE VIRTUAL TABLE knowledge_fts USING fts5(meeting_id UNINDEXED, kind, content);

            CREATE TRIGGER IF NOT EXISTS transcript_fts_insert AFTER INSERT ON transcript_segments BEGIN
                DELETE FROM transcript_fts WHERE meeting_id = new.meeting_id;
                INSERT INTO transcript_fts (meeting_id, text) VALUES (new.meeting_id, new.text);
            END;
            CREATE TRIGGER IF NOT EXISTS transcript_fts_delete AFTER DELETE ON transcript_segments BEGIN
                DELETE FROM transcript_fts WHERE meeting_id = old.meeting_id;
            END;

            CREATE TRIGGER IF NOT EXISTS knowledge_fts_decision_insert AFTER INSERT ON decisions BEGIN
                DELETE FROM knowledge_fts WHERE meeting_id = new.meeting_id AND kind = 'decisions';
                INSERT INTO knowledge_fts (meeting_id, kind, content) VALUES (new.meeting_id, 'decisions', new.decision_text);
            END;
            CREATE TRIGGER IF NOT EXISTS knowledge_fts_decision_delete AFTER DELETE ON decisions BEGIN
                DELETE FROM knowledge_fts WHERE meeting_id = old.meeting_id AND kind = 'decisions';
            END;
            CREATE TRIGGER IF NOT EXISTS knowledge_fts_action_insert AFTER INSERT ON action_items BEGIN
                DELETE FROM knowledge_fts WHERE meeting_id = new.meeting_id AND kind = 'actions';
                INSERT INTO knowledge_fts (meeting_id, kind, content) VALUES (new.meeting_id, 'actions', new.task);
            END;
            CREATE TRIGGER IF NOT EXISTS knowledge_fts_action_delete AFTER DELETE ON action_items BEGIN
                DELETE FROM knowledge_fts WHERE meeting_id = old.meeting_id AND kind = 'actions';
            END;
            CREATE TRIGGER IF NOT EXISTS knowledge_fts_question_insert AFTER INSERT ON open_questions BEGIN
                DELETE FROM knowledge_fts WHERE meeting_id = new.meeting_id AND kind = 'questions';
                INSERT INTO knowledge_fts (meeting_id, kind, content) VALUES (new.meeting_id, 'questions', new.question);
            END;
            CREATE TRIGGER IF NOT EXISTS knowledge_fts_question_delete AFTER DELETE ON open_questions BEGIN
                DELETE FROM knowledge_fts WHERE meeting_id = old.meeting_id AND kind = 'questions';
            END;
            """),
    ];
}
