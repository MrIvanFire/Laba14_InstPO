using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace SimpleApiServer
{
    // модель задачи как в задании
    public class TaskItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool IsCompleted { get; set; }
        // новые поля для 11 лабораторной
        public string Category { get; set; }
        public string Location { get; set; }
        // добавляем новые поля для расширенной валидации
        public int Priority { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // DTO для создания задачи (тело запроса)
    public class CreateTaskRequest
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public bool IsCompleted { get; set; }
        public string Category { get; set; }
        public string Location { get; set; }
        public int Priority { get; set; }
        public DateTime DueDate { get; set; }
    }

    // DTO для обновления задачи
    public class UpdateTaskRequest
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public bool IsCompleted { get; set; }
        public string Category { get; set; }
        public string Location { get; set; }
        public int Priority { get; set; }
        public DateTime DueDate { get; set; }
    }

    // Класс для ошибки валидации
    public class ValidationError
    {
        public string field { get; set; }
        public string message { get; set; }
    }

    // Класс для ответа с ошибкой валидации
    public class ErrorResponse
    {
        public string error { get; set; }
        public List<ValidationError> errors { get; set; }
    }

    // модель пользователя для JWT
    public class User
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public string Name { get; set; }
    }

    // DTO для регистрации
    public class RegisterRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string Name { get; set; }
    }

    // DTO для логина
    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    // DTO для ответа с токеном
    public class AuthResponse
    {
        public string Token { get; set; }
        public string Email { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    class Program
    {
        // хранилище задач в памяти
        static List<TaskItem> _tasks = new List<TaskItem>();
        // хранилище пользователей
        static List<User> _users = new List<User>();
        // счетчик чтобы давать новые номера задачам
        static int _nextId = 1;
        // счетчик для пользователей
        static int _nextUserId = 1;
        // настройки сериализации чтобы json был в camelCase
        static JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true // для красивых ответов
        };

        // JWT настройки
        static string _jwtSecret = "mySuperSecretKeyForJWT12345678901234567890"; // в реальном проекте в конфиг
        static string _jwtIssuer = "SimpleApiServer";
        static string _jwtAudience = "SimpleApiClient";
        static int _jwtLifetimeMinutes = 60;

        static async Task Main(string[] args)
        {
            // добавляем тестовые задачи с новыми полями
            _tasks.Add(new TaskItem
            {
                Id = _nextId++,
                Title = "Make laba",
                Description = "Complete laboratory work 12.2",
                IsCompleted = false,
                Category = "study",
                Location = "university",
                Priority = 3,
                DueDate = DateTime.Now.AddDays(2),
                CreatedAt = DateTime.Now
            });
            _tasks.Add(new TaskItem
            {
                Id = _nextId++,
                Title = "Buy Bread",
                Description = "Buy whole wheat bread",
                IsCompleted = true,
                Category = "shopping",
                Location = "home",
                Priority = 2,
                DueDate = DateTime.Now,
                CreatedAt = DateTime.Now
            });
            _tasks.Add(new TaskItem
            {
                Id = _nextId++,
                Title = "Call dad",
                Description = "Call dad on weekend",
                IsCompleted = false,
                Category = "personal",
                Location = "home",
                Priority = 5,
                DueDate = DateTime.Now.AddDays(1),
                CreatedAt = DateTime.Now
            });

            // создаем слушателя http
            HttpListener listener = new HttpListener();
            // слушаем порт 8000 (чтобы не конфликтовать с другими приложениями)
            listener.Prefixes.Add("http://localhost:8000/");
            listener.Start();

            Console.WriteLine("сервер запущен на http://localhost:8000/");
            Console.WriteLine("доступные пути: /api/tasks и /api/tasks/{id}");
            Console.WriteLine("новые пути: /api/auth/register и /api/auth/login");
            Console.WriteLine("для выхода нажми ctrl+c");

            // вечный цикл ожидания запросов
            while (true)
            {
                try
                {
                    // ждем входящее соединение
                    HttpListenerContext context = await listener.GetContextAsync();
                    // обрабатываем запрос в отдельной функции
                    ProcessRequest(context);
                }
                catch (Exception ex)
                {
                    // выводим ошибку если что-то пошло не так
                    Console.WriteLine("ошибка обработки: " + ex.Message);
                }
            }
        }

        // функция распределения запросов по методам
        // добавлен try-catch для централизованной обработки ошибок 500 (требование лабы 14.2)
        static void ProcessRequest(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            try
            {
                string path = request.Url.AbsolutePath;
                string method = request.HttpMethod;

                // проверяем путь и метод чтобы выбрать нужную функцию
                if (path == "/api/tasks" && method == "GET")
                {
                    HandleGetTasks(request, response);
                }
                else if (path.StartsWith("/api/tasks/") && method == "GET")
                {
                    // достаем id из пути например /api/tasks/1
                    int id = GetIdFromPath(path);
                    HandleGetTaskById(request, response, id);
                }
                else if (path == "/api/tasks" && method == "POST")
                {
                    // защищенный маршрут - проверяем токен
                    if (!ValidateTokenAndAuthorize(request, response))
                        return;
                    HandleCreateTask(request, response);
                }
                else if (path.StartsWith("/api/tasks/") && method == "PUT")
                {
                    // защищенный маршрут - проверяем токен (добавлено по требованию лабы 14.2)
                    if (!ValidateTokenAndAuthorize(request, response))
                        return;
                    int id = GetIdFromPath(path);
                    HandleUpdateTask(request, response, id);
                }
                else if (path.StartsWith("/api/tasks/") && method == "DELETE")
                {
                    // защищенный маршрут - проверяем токен (добавлено по требованию лабы 14.2)
                    if (!ValidateTokenAndAuthorize(request, response))
                        return;
                    int id = GetIdFromPath(path);
                    HandleDeleteTask(request, response, id);
                }
                else if (path == "/api/auth/register" && method == "POST")
                {
                    HandleRegister(request, response);
                }
                else if (path == "/api/auth/login" && method == "POST")
                {
                    HandleLogin(request, response);
                }
                else
                {
                    // если путь не найден возвращаем 404
                    WriteErrorResponse(response, 404, "endpoint not found", new List<ValidationError>());
                }
            }
            catch (Exception ex)
            {
                // централизованная обработка любых необработанных исключений (требование лабы 14.2)
                Console.WriteLine($"КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}");
                Console.WriteLine($"STACK TRACE: {ex.StackTrace}");

                // возвращаем единый формат ошибки 500 как требует лабораторная
                var errorResponse = new
                {
                    error = "InternalServerError",
                    message = "Произошла внутренняя ошибка сервера"
                };
                WriteJson(response, errorResponse, 500);
            }
        }

        // вспомогательная функция чтобы вытащить число из пути
        static int GetIdFromPath(string path)
        {
            string[] parts = path.Split('/');
            if (parts.Length > 0 && int.TryParse(parts[parts.Length - 1], out int id))
            {
                return id;
            }
            return -1;
        }

        // GET /api/tasks - список всех задач с фильтрацией и пагинацией
        static void HandleGetTasks(HttpListenerRequest request, HttpListenerResponse response)
        {
            var query = request.QueryString;
            var tasks = _tasks.AsEnumerable();

            // фильтр по isCompleted
            if (!string.IsNullOrEmpty(query["isCompleted"]))
            {
                if (bool.TryParse(query["isCompleted"], out bool isCompleted))
                {
                    tasks = tasks.Where(t => t.IsCompleted == isCompleted);
                }
            }

            // фильтр по category
            if (!string.IsNullOrEmpty(query["category"]))
            {
                string category = query["category"];
                tasks = tasks.Where(t => t.Category != null && t.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
            }

            // фильтр по location
            if (!string.IsNullOrEmpty(query["location"]))
            {
                string location = query["location"];
                tasks = tasks.Where(t => t.Location != null && t.Location.Equals(location, StringComparison.OrdinalIgnoreCase));
            }

            // фильтр по priority
            if (!string.IsNullOrEmpty(query["priority"]))
            {
                if (int.TryParse(query["priority"], out int priority))
                {
                    tasks = tasks.Where(t => t.Priority == priority);
                }
            }

            // пагинация
            int page = 1;
            int pageSize = 10;
            if (!string.IsNullOrEmpty(query["page"]))
                int.TryParse(query["page"], out page);
            if (!string.IsNullOrEmpty(query["pageSize"]))
                int.TryParse(query["pageSize"], out pageSize);

            page = Math.Max(1, page);
            // заменяем Math.Clamp на ручную проверку
            if (pageSize < 1) pageSize = 1;
            if (pageSize > 50) pageSize = 50;

            // сортировка
            if (!string.IsNullOrEmpty(query["orderBy"]))
            {
                string direction = query["direction"] ?? "asc";
                bool desc = direction.Equals("desc", StringComparison.OrdinalIgnoreCase);

                switch (query["orderBy"].ToLower())
                {
                    case "title":
                        tasks = desc ? tasks.OrderByDescending(t => t.Title) : tasks.OrderBy(t => t.Title);
                        break;
                    case "category":
                        tasks = desc ? tasks.OrderByDescending(t => t.Category) : tasks.OrderBy(t => t.Category);
                        break;
                    case "priority":
                        tasks = desc ? tasks.OrderByDescending(t => t.Priority) : tasks.OrderBy(t => t.Priority);
                        break;
                    case "duedate":
                        tasks = desc ? tasks.OrderByDescending(t => t.DueDate) : tasks.OrderBy(t => t.DueDate);
                        break;
                }
            }

            int total = tasks.Count();

            // применяем пагинацию
            var pagedTasks = tasks
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // возвращаем с метаданными
            var result = new
            {
                data = pagedTasks,
                total = total,
                page = page,
                pageSize = pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize)
            };

            WriteSuccessResponse(response, result, 200);
        }

        // GET /api/tasks/{id} - одна задача
        static void HandleGetTaskById(HttpListenerRequest request, HttpListenerResponse response, int id)
        {
            // ищем задачу по номеру
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null)
            {
                // если не нашли возвращаем 404
                var errors = new List<ValidationError>
                {
                    new ValidationError { field = "id", message = $"Task with id {id} not found" }
                };
                WriteErrorResponse(response, 404, "Task not found", errors);
            }
            else
            {
                // если нашли возвращаем задачу
                WriteSuccessResponse(response, task, 200);
            }
        }

        // POST /api/tasks - создание задачи с валидацией
        // валидация возвращает 400 с единым форматом JSON (требование лабы 14.2)
        static void HandleCreateTask(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                // читаем тело запроса
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    string body = reader.ReadToEnd();

                    // проверяем что тело не пустое
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        var errors = new List<ValidationError>
                        {
                            new ValidationError { field = "body", message = "Request body cannot be empty" }
                        };
                        WriteErrorResponse(response, 400, "Validation error", errors);
                        return;
                    }

                    // превращаем json в объект DTO
                    CreateTaskRequest requestData;
                    try
                    {
                        requestData = JsonSerializer.Deserialize<CreateTaskRequest>(body, _jsonOptions);
                    }
                    catch (JsonException)
                    {
                        var errors = new List<ValidationError>
                        {
                            new ValidationError { field = "json", message = "Invalid JSON format" }
                        };
                        WriteErrorResponse(response, 400, "Validation error", errors);
                        return;
                    }

                    // валидация: собираем все ошибки (ручная валидация по требованию лабы)
                    var validationErrors = new List<ValidationError>();

                    // проверка Title (обязательное поле, длина от 1 до 200)
                    if (requestData == null || string.IsNullOrWhiteSpace(requestData.Title))
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "title",
                            message = "Title is required and cannot be empty"
                        });
                    }
                    else if (requestData.Title.Length > 200)
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "title",
                            message = "Title cannot exceed 200 characters"
                        });
                    }
                    else if (requestData.Title.Length < 1)
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "title",
                            message = "Title must be at least 1 character"
                        });
                    }

                    // проверка Description (опционально, но если есть - не больше 1000 символов)
                    if (requestData != null && !string.IsNullOrEmpty(requestData.Description) && requestData.Description.Length > 1000)
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "description",
                            message = "Description cannot exceed 1000 characters"
                        });
                    }

                    if (requestData != null && requestData.Priority != 0)
                    {
                        if (requestData.Priority < 1 || requestData.Priority > 5)
                        {
                            validationErrors.Add(new ValidationError
                            {
                                field = "priority",
                                message = "Priority must be between 1 and 5"
                            });
                        }
                    }

                    if (requestData != null && !string.IsNullOrEmpty(requestData.Category) && requestData.Category.Length > 50)
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "category",
                            message = "Category cannot exceed 50 characters"
                        });
                    }

                    if (requestData != null && !string.IsNullOrEmpty(requestData.Location) && requestData.Location.Length > 100)
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "location",
                            message = "Location cannot exceed 100 characters"
                        });
                    }

                    if (requestData != null && requestData.DueDate != DateTime.MinValue && requestData.DueDate < DateTime.Now)
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "dueDate",
                            message = "Due date cannot be in the past"
                        });
                    }

                    // если есть ошибки валидации - возвращаем 400 с JSON (требование лабы)
                    if (validationErrors.Count > 0)
                    {
                        WriteErrorResponse(response, 400, "Validation error", validationErrors);
                        return;
                    }

                    var newTask = new TaskItem
                    {
                        Id = _nextId++,
                        Title = requestData.Title,
                        Description = requestData.Description ?? string.Empty,
                        IsCompleted = requestData.IsCompleted,
                        Category = string.IsNullOrEmpty(requestData.Category) ? "general" : requestData.Category,
                        Location = string.IsNullOrEmpty(requestData.Location) ? "unknown" : requestData.Location,
                        Priority = requestData.Priority == 0 ? 3 : requestData.Priority,
                        DueDate = requestData.DueDate,
                        CreatedAt = DateTime.Now
                    };

                    _tasks.Add(newTask);
                    WriteSuccessResponse(response, newTask, 201);
                }
            }
            catch (Exception ex)
            {
                // обработка исключений с возвратом 500 (требование лабы 14.2)
                var errors = new List<ValidationError>
                {
                    new ValidationError { field = "server", message = $"Internal server error: {ex.Message}" }
                };
                WriteErrorResponse(response, 500, "Server error", errors);
            }
        }

        // PUT /api/tasks/{id} - обновление задачи с валидацией
        static void HandleUpdateTask(HttpListenerRequest request, HttpListenerResponse response, int id)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null)
            {
                var errors = new List<ValidationError>
                {
                    new ValidationError { field = "id", message = $"Task with id {id} not found" }
                };
                WriteErrorResponse(response, 404, "Task not found", errors);
                return;
            }

            try
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    string body = reader.ReadToEnd();

                    if (string.IsNullOrWhiteSpace(body))
                    {
                        var errors = new List<ValidationError>
                        {
                            new ValidationError { field = "body", message = "Request body cannot be empty" }
                        };
                        WriteErrorResponse(response, 400, "Validation error", errors);
                        return;
                    }

                    UpdateTaskRequest updateData;
                    try
                    {
                        updateData = JsonSerializer.Deserialize<UpdateTaskRequest>(body, _jsonOptions);
                    }
                    catch (JsonException)
                    {
                        var errors = new List<ValidationError>
                        {
                            new ValidationError { field = "json", message = "Invalid JSON format" }
                        };
                        WriteErrorResponse(response, 400, "Validation error", errors);
                        return;
                    }

                    // ручная валидация для PUT
                    var validationErrors = new List<ValidationError>();

                    if (updateData != null && updateData.Title != null)
                    {
                        if (string.IsNullOrWhiteSpace(updateData.Title))
                        {
                            validationErrors.Add(new ValidationError
                            {
                                field = "title",
                                message = "Title cannot be empty"
                            });
                        }
                        else if (updateData.Title.Length > 200)
                        {
                            validationErrors.Add(new ValidationError
                            {
                                field = "title",
                                message = "Title cannot exceed 200 characters"
                            });
                        }
                    }

                    if (updateData != null && !string.IsNullOrEmpty(updateData.Description) && updateData.Description.Length > 1000)
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "description",
                            message = "Description cannot exceed 1000 characters"
                        });
                    }

                    if (updateData != null && updateData.Priority != 0)
                    {
                        if (updateData.Priority < 1 || updateData.Priority > 5)
                        {
                            validationErrors.Add(new ValidationError
                            {
                                field = "priority",
                                message = "Priority must be between 1 and 5"
                            });
                        }
                    }

                    if (updateData != null && !string.IsNullOrEmpty(updateData.Category) && updateData.Category.Length > 50)
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "category",
                            message = "Category cannot exceed 50 characters"
                        });
                    }

                    if (updateData != null && !string.IsNullOrEmpty(updateData.Location) && updateData.Location.Length > 100)
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "location",
                            message = "Location cannot exceed 100 characters"
                        });
                    }

                    if (updateData != null && updateData.DueDate != DateTime.MinValue && updateData.DueDate < DateTime.Now)
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "dueDate",
                            message = "Due date cannot be in the past"
                        });
                    }

                    if (validationErrors.Count > 0)
                    {
                        WriteErrorResponse(response, 400, "Validation error", validationErrors);
                        return;
                    }

                    if (updateData != null)
                    {
                        if (updateData.Title != null)
                            task.Title = updateData.Title;

                        if (updateData.Description != null)
                            task.Description = updateData.Description;

                        task.IsCompleted = updateData.IsCompleted;

                        if (updateData.Category != null)
                            task.Category = updateData.Category;

                        if (updateData.Location != null)
                            task.Location = updateData.Location;

                        if (updateData.Priority != 0)
                            task.Priority = updateData.Priority;

                        if (updateData.DueDate != DateTime.MinValue)
                            task.DueDate = updateData.DueDate;
                    }

                    WriteSuccessResponse(response, task, 200);
                }
            }
            catch (Exception ex)
            {
                var errors = new List<ValidationError>
                {
                    new ValidationError { field = "server", message = $"Internal server error: {ex.Message}" }
                };
                WriteErrorResponse(response, 500, "Server error", errors);
            }
        }

        // DELETE /api/tasks/{id} - удаление задачи
        static void HandleDeleteTask(HttpListenerRequest request, HttpListenerResponse response, int id)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null)
            {
                var errors = new List<ValidationError>
                {
                    new ValidationError { field = "id", message = $"Task with id {id} not found" }
                };
                WriteErrorResponse(response, 404, "Task not found", errors);
                return;
            }

            _tasks.Remove(task);
            WriteSuccessResponse(response, new { message = "Task deleted successfully" }, 200);
        }

        // хэширование пароля
        static string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        // генерация JWT токена
        static string GenerateJwtToken(User user)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<System.Security.Claims.Claim>
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, user.Email)
            };

            var token = new JwtSecurityToken(
                issuer: _jwtIssuer,
                audience: _jwtAudience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_jwtLifetimeMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // проверка JWT токена
        static bool ValidateTokenAndAuthorize(HttpListenerRequest request, HttpListenerResponse response)
        {
            string authHeader = request.Headers["Authorization"];

            // проверяем наличие заголовка
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                WriteUnauthorizedResponse(response, "Требуется авторизация");
                return false;
            }

            string token = authHeader.Substring("Bearer ".Length);

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret)),
                    ValidateIssuer = true,
                    ValidIssuer = _jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = _jwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                tokenHandler.ValidateToken(token, validationParameters, out _);
                return true;
            }
            catch (Exception)
            {
                WriteUnauthorizedResponse(response, "Невалидный токен");
                return false;
            }
        }

        // ответ при 401
        static void WriteUnauthorizedResponse(HttpListenerResponse response, string message)
        {
            var errorResponse = new
            {
                error = "Unauthorized",
                message = message
            };
            WriteJson(response, errorResponse, 401);
        }

        // POST /api/auth/register - регистрация нового пользователя
        static void HandleRegister(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    string body = reader.ReadToEnd();

                    if (string.IsNullOrWhiteSpace(body))
                    {
                        var errors = new List<ValidationError>
                        {
                            new ValidationError { field = "body", message = "Request body cannot be empty" }
                        };
                        WriteErrorResponse(response, 400, "Validation error", errors);
                        return;
                    }

                    RegisterRequest registerData;
                    try
                    {
                        registerData = JsonSerializer.Deserialize<RegisterRequest>(body, _jsonOptions);
                    }
                    catch (JsonException)
                    {
                        var errors = new List<ValidationError>
                        {
                            new ValidationError { field = "json", message = "Invalid JSON format" }
                        };
                        WriteErrorResponse(response, 400, "Validation error", errors);
                        return;
                    }

                    // валидация
                    var validationErrors = new List<ValidationError>();

                    if (string.IsNullOrWhiteSpace(registerData.Email))
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "email",
                            message = "Email is required"
                        });
                    }
                    else if (!registerData.Email.Contains("@"))
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "email",
                            message = "Invalid email format"
                        });
                    }

                    if (string.IsNullOrWhiteSpace(registerData.Password))
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "password",
                            message = "Password is required"
                        });
                    }
                    else if (registerData.Password.Length < 6)
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "password",
                            message = "Password must be at least 6 characters"
                        });
                    }

                    // проверяем уникальность email
                    if (_users.Any(u => u.Email.Equals(registerData.Email, StringComparison.OrdinalIgnoreCase)))
                    {
                        validationErrors.Add(new ValidationError
                        {
                            field = "email",
                            message = "User with this email already exists"
                        });
                    }

                    if (validationErrors.Count > 0)
                    {
                        WriteErrorResponse(response, 400, "Validation error", validationErrors);
                        return;
                    }

                    // создаем пользователя
                    var newUser = new User
                    {
                        Id = _nextUserId++,
                        Email = registerData.Email,
                        PasswordHash = HashPassword(registerData.Password),
                        Name = registerData.Name ?? string.Empty
                    };

                    _users.Add(newUser);

                    // генерируем токен
                    string token = GenerateJwtToken(newUser);
                    var authResponse = new AuthResponse
                    {
                        Token = token,
                        Email = newUser.Email,
                        ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtLifetimeMinutes)
                    };

                    WriteSuccessResponse(response, authResponse, 201);
                }
            }
            catch (Exception ex)
            {
                var errors = new List<ValidationError>
                {
                    new ValidationError { field = "server", message = $"Internal server error: {ex.Message}" }
                };
                WriteErrorResponse(response, 500, "Server error", errors);
            }
        }

        // POST /api/auth/login - логин пользователя
        static void HandleLogin(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    string body = reader.ReadToEnd();

                    if (string.IsNullOrWhiteSpace(body))
                    {
                        var errors = new List<ValidationError>
                        {
                            new ValidationError { field = "body", message = "Request body cannot be empty" }
                        };
                        WriteErrorResponse(response, 400, "Validation error", errors);
                        return;
                    }

                    LoginRequest loginData;
                    try
                    {
                        loginData = JsonSerializer.Deserialize<LoginRequest>(body, _jsonOptions);
                    }
                    catch (JsonException)
                    {
                        var errors = new List<ValidationError>
                        {
                            new ValidationError { field = "json", message = "Invalid JSON format" }
                        };
                        WriteErrorResponse(response, 400, "Validation error", errors);
                        return;
                    }

                    // ищем пользователя
                    var user = _users.FirstOrDefault(u => u.Email.Equals(loginData.Email, StringComparison.OrdinalIgnoreCase));

                    if (user == null || user.PasswordHash != HashPassword(loginData.Password))
                    {
                        WriteUnauthorizedResponse(response, "Неверный email или пароль");
                        return;
                    }

                    // генерируем токен
                    string token = GenerateJwtToken(user);
                    var authResponse = new AuthResponse
                    {
                        Token = token,
                        Email = user.Email,
                        ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtLifetimeMinutes)
                    };

                    WriteSuccessResponse(response, authResponse, 200);
                }
            }
            catch (Exception ex)
            {
                var errors = new List<ValidationError>
                {
                    new ValidationError { field = "server", message = $"Internal server error: {ex.Message}" }
                };
                WriteErrorResponse(response, 500, "Server error", errors);
            }
        }

        // успешный ответ с данными
        static void WriteSuccessResponse<T>(HttpListenerResponse response, T data, int statusCode)
        {
            var result = new
            {
                data = data,
                error = (string)null
            };
            WriteJson(response, result, statusCode);
        }

        // ответ с ошибкой (единый формат для 400 и 500)
        static void WriteErrorResponse(HttpListenerResponse response, int statusCode, string errorMessage, List<ValidationError> errors)
        {
            var errorResponse = new ErrorResponse
            {
                error = errorMessage,
                errors = errors
            };
            WriteJson(response, errorResponse, statusCode);
        }

        // отправка JSON ответа
        static void WriteJson<T>(HttpListenerResponse response, T data, int statusCode)
        {
            try
            {
                string json = JsonSerializer.Serialize(data, _jsonOptions);
                byte[] buffer = Encoding.UTF8.GetBytes(json);

                response.StatusCode = statusCode;
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = buffer.Length;

                using (var output = response.OutputStream)
                {
                    output.Write(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing response: {ex.Message}");
            }
        }
    }
}