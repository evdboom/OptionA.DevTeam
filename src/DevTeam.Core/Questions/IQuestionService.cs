namespace DevTeam.Core;

public interface IQuestionService
{
    QuestionItem AddQuestion(WorkspaceState state, string text, bool blocking);
    void AnswerQuestion(WorkspaceState state, int questionId, string answer);
    IReadOnlyList<QuestionItem> AddQuestions(WorkspaceState state, IEnumerable<ProposedQuestion> questions);
}
