namespace Api.Features.Auth
{
    /// <summary>
    /// The 401 every protected route answers with: an RFC 9457 problem document with the
    /// human message in 'detail' (the same shape the exception handler produces).
    /// </summary>
    internal static class UnauthorizedResponse
    {
        public const string NotSignedInDetail = "Not signed in.";
        public const string IncorrectPasswordDetail = "Incorrect password.";

        /// <summary>
        /// The title of the 409 a signed-in caller gets for a wrong current password: the
        /// session is valid, so a 401 (which the UI answers by switching to the login page)
        /// would sign the caller out instead of letting them fix the input.
        /// </summary>
        public const string IncorrectCurrentPasswordTitle = "Incorrect current password";

        public static IResult Problem(string detail) => Results.Problem(
            title: "Unauthorized",
            detail: detail,
            statusCode: StatusCodes.Status401Unauthorized);

        public static IResult Conflict(string detail) => Results.Problem(
            title: IncorrectCurrentPasswordTitle,
            detail: detail,
            statusCode: StatusCodes.Status409Conflict);
    }
}
