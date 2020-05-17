namespace UB3RB0T.Config
{
    /// <summary>
    /// Assistant to the Resource Manager
    /// </summary>
    public static class StringHelper
    {
       public static string GetString(string stringName)
        {
            return Strings.ResourceManager.GetString(stringName);
        }
    }
}
