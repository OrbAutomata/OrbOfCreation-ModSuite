public sealed class AlchemyManager
{
    public static AlchemyManager? instance;
    public AlchemyInstanceListVariable activeAlchemy = new AlchemyInstanceListVariable();
    public AlchemyRecipeListVariable allAlchemy = new AlchemyRecipeListVariable();
}
