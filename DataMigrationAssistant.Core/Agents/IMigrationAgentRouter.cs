namespace DataMigrationAssistant.Core.Agents;

public interface IMigrationAgentRouter
{
    IMigrationAgent Route(string question);
}
