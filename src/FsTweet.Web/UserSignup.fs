namespace UserSignup

module Domain =
  open Chessie.ErrorHandling
  open BCrypt.Net

  type Username = private Username of string with
    static member TryCreate (username : string) =
      match username with
      | null | ""  -> fail "Username should not be empty"
      | x when x.Length > 12 -> fail "Username should not be more than 12 characters"
      | x -> Username x |> ok
    member this.Value = 
      let (Username username) = this
      username

  type EmailAddress = private EmailAddress of string with
    member this.Value =
      let (EmailAddress emailAddress) = this
      emailAddress
    static member TryCreate (emailAddress : string) =
     try 
       new System.Net.Mail.MailAddress(emailAddress) |> ignore
       EmailAddress emailAddress |> ok
     with
       | _ -> fail "Invalid Email Address"

  type Password = private Password of string with 
    member this.Value =
      let (Password password) = this
      password
    static member TryCreate (password : string) =
      match password with
      | null | ""  -> fail "Password should not be empty"
      | x when x.Length < 4 || x.Length > 8 -> fail "Password should contain only 4-8 characters"
      | x -> Password x |> ok

  type UserSignupRequest = {
    Username : Username
    Password : Password
    EmailAddress : EmailAddress
  }
  with static member TryCreate (username, password, email) =
        trial {
          let! username = Username.TryCreate username
          let! password = Password.TryCreate password
          let! emailAddress = EmailAddress.TryCreate email
          return {
            Username = username
            Password = password
            EmailAddress = emailAddress
          }
        }

  type PasswordHash = private PasswordHash of string with
    member this.Value =
      let (PasswordHash passwordHash) = this
      passwordHash

    member this.Match password =
      BCrypt.Verify(password, this.Value) 

    static member Create (password : Password) =
      BCrypt.HashPassword(password.Value)
      |> PasswordHash


  type VerificationCode = VerificationCode of string
  type CreateUserRequest = {
    Username : Username
    PasswordHash : PasswordHash
    Email : EmailAddress
    VerificationCode : VerificationCode
  }

  type UserId = UserId of int

  type Error = System.Exception

  type CreateUserError =
  | EmailAlreadyExists
  | UsernameAlreadyExists
  | Error of Error

  type CreateUserResponse = {
    UserId : UserId
    VerificationCode : VerificationCode
  }
  type CreateUser = 
    CreateUserRequest -> AsyncResult<CreateUserResponse, CreateUserError>

  type SignupEmailRequest = {
    Username : Username
    VerificationCode : VerificationCode
  }
  type SendEmailError = SendEmailError of Error
  type SendSignupEmail = SignupEmailRequest -> AsyncResult<unit, SendEmailError>

  type UserSignupError =
  | CreateUserError of CreateUserError
  | SendEmailError of SendEmailError

  let mapFailure f = 
    List.head >> f >> List.singleton |> mapFailure

  let mapAsyncFailure f =
    Async.ofAsyncResult >> Async.map (mapFailure f) >> AR

  let createUser (createUser : CreateUser) 
                 (sendEmail : SendSignupEmail) 
                 (req : UserSignupRequest) = asyncTrial {

    let verificationCode = VerificationCode ""

    let createUserReq = {
      PasswordHash = PasswordHash.Create req.Password
      Username = req.Username
      Email = req.EmailAddress
      VerificationCode = verificationCode
    }
    let! res = 
      createUser createUserReq
      |> mapAsyncFailure CreateUserError 

    let sendEmailReq = {
      Username = req.Username
      VerificationCode = verificationCode
    }
    let! sendEmailRes = 
      sendEmail sendEmailReq
      |> mapAsyncFailure SendEmailError

    return res.UserId
  }

module Suave =
  open Suave
  open Suave.Filters
  open Suave.Operators
  open Suave.DotLiquid
  open Suave.Form
  open Domain
  open Chessie.ErrorHandling

  type UserSignupViewModel = {
    Username : string
    Email : string
    Password: string
    Error : string option
  }  
  let emptyUserSignupViewModel = {
    Username = ""
    Email = ""
    Password = ""
    Error = None
  }

  let signupTemplatePath = "user/signup.liquid" 

  let handleUserSignup ctx = async {
    match bindEmptyForm ctx.request with
    | Choice1Of2 (vm : UserSignupViewModel) ->
      let result =
        UserSignupRequest.TryCreate (vm.Username, vm.Password, vm.Email)
      let onSuccess (userSignupReq, _) =
        printfn "%A" userSignupReq
        Redirection.FOUND "/signup" ctx
      let onFailure msgs =
        let viewModel = {vm with Error = Some (List.head msgs)}
        page signupTemplatePath viewModel ctx
      return! either onSuccess onFailure result
    | Choice2Of2 err ->
      let viewModel = {emptyUserSignupViewModel with Error = Some err}
      return! page signupTemplatePath viewModel ctx
  }

  let webPart () =
    path "/signup" 
      >=> choose [
        GET >=> page signupTemplatePath emptyUserSignupViewModel
        POST >=> handleUserSignup
      ]
      

