namespace Wordset.Domain.Exceptions;

public class WordsetNotFoundException(string identifier)
    : Exception($"Wordset '{identifier}' was not found.");
