# GymFlowPro Flutter Integration Guide

## Quick Start

### 1. Setup Dependencies

Add to `pubspec.yaml`:

```yaml
dependencies:
  http: ^1.1.0
  flutter_secure_storage: ^9.0.0
  jwt_decoder: ^2.0.1
  provider: ^6.0.0
  connectivity_plus: ^5.0.0
  intl: ^0.19.0
  dio: ^5.3.0  # Alternative HTTP client (optional)

dev_dependencies:
  flutter_test:
    sdk: flutter
```

---

## Authentication Flow

### 1. Member OTP Login Flow

```dart
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;
import 'dart:convert';

class AuthService {
  static const String baseUrl = 'http://localhost:5000/api/auth';
  final storage = const FlutterSecureStorage();

  /// Step 1: Request OTP
  Future<bool> sendMemberOtp(String phoneNumber) async {
    try {
      final response = await http.post(
        Uri.parse('$baseUrl/member-otp'),
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({'phoneNumber': phoneNumber}),
      );

      if (response.statusCode == 200) {
        // OTP sent successfully, show OTP input screen
        return true;
      } else {
        final error = jsonDecode(response.body);
        throw Exception(error['error'] ?? 'Failed to send OTP');
      }
    } catch (e) {
      print('Error sending OTP: $e');
      return false;
    }
  }

  /// Step 2: Verify OTP and get token
  Future<bool> verifyMemberOtp(String phoneNumber, String otp) async {
    try {
      final response = await http.post(
        Uri.parse('$baseUrl/member-verify'),
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({
          'phoneNumber': phoneNumber,
          'otp': otp,
        }),
      );

      if (response.statusCode == 200) {
        final data = jsonDecode(response.body);
        
        // Store tokens securely
        await storage.write(key: 'access_token', value: data['accessToken']);
        await storage.write(key: 'refresh_token', value: data['refreshToken']);
        await storage.write(key: 'user_id', value: data['user']['id']);
        
        return true;
      } else {
        final error = jsonDecode(response.body);
        throw Exception(error['error'] ?? 'Invalid OTP');
      }
    } catch (e) {
      print('Error verifying OTP: $e');
      return false;
    }
  }

  /// Get stored access token
  Future<String?> getAccessToken() async {
    return await storage.read(key: 'access_token');
  }

  /// Refresh token when expired
  Future<bool> refreshToken() async {
    try {
      final refreshToken = await storage.read(key: 'refresh_token');
      
      final response = await http.post(
        Uri.parse('$baseUrl/refresh'),
        headers: {'Content-Type': 'application/json'},
        body: jsonEncode({'refreshToken': refreshToken}),
      );

      if (response.statusCode == 200) {
        final data = jsonDecode(response.body);
        await storage.write(key: 'access_token', value: data['accessToken']);
        await storage.write(key: 'refresh_token', value: data['refreshToken']);
        return true;
      }
      return false;
    } catch (e) {
      print('Error refreshing token: $e');
      return false;
    }
  }

  /// Logout
  Future<void> logout() async {
    await storage.delete(key: 'access_token');
    await storage.delete(key: 'refresh_token');
    await storage.delete(key: 'user_id');
  }
}
```

---

## QR Code Check-in

### Generate QR Code (Static - One per gym)

On the gym's static QR code, encode:
```
gymnasium://checkin?gymCode=GYM-TEST-01&qrToken=static-token-value
```

### Process QR Scan

```dart
import 'package:mobile_scanner/mobile_scanner.dart';

class QrCheckinService {
  static const String baseUrl = 'http://localhost:5000/api/attendance';
  final authService = AuthService();

  Future<CheckinResponse?> processQrCheckin(String qrCode) async {
    try {
      // Parse QR code
      final uri = Uri.parse(qrCode);
      final qrToken = uri.queryParameters['qrToken'];
      
      if (qrToken == null) {
        throw Exception('Invalid QR code format');
      }

      // Get access token
      final token = await authService.getAccessToken();
      if (token == null) {
        throw Exception('Not authenticated');
      }

      // Send check-in request
      final response = await http.post(
        Uri.parse('$baseUrl/qr-checkin'),
        headers: {
          'Content-Type': 'application/json',
          'Authorization': 'Bearer $token',
        },
        body: jsonEncode({
          'qrToken': qrToken,
        }),
      ).timeout(const Duration(seconds: 10));

      if (response.statusCode == 200) {
        final data = jsonDecode(response.body);
        return CheckinResponse.fromJson(data);
      } else if (response.statusCode == 429) {
        throw Exception('Check-in too soon. Please wait before trying again.');
      } else if (response.statusCode == 400) {
        final error = jsonDecode(response.body);
        throw Exception(error['error'] ?? 'Check-in failed');
      } else {
        throw Exception('Server error: ${response.statusCode}');
      }
    } catch (e) {
      print('QR check-in error: $e');
      return null;
    }
  }
}

class CheckinResponse {
  final bool success;
  final String message;
  final String attendanceId;
  final DateTime checkInTime;
  final String memberName;
  final String planType;
  final String? remainingTime;

  CheckinResponse({
    required this.success,
    required this.message,
    required this.attendanceId,
    required this.checkInTime,
    required this.memberName,
    required this.planType,
    this.remainingTime,
  });

  factory CheckinResponse.fromJson(Map<String, dynamic> json) {
    return CheckinResponse(
      success: json['success'] ?? false,
      message: json['message'] ?? '',
      attendanceId: json['attendanceId'] ?? '',
      checkInTime: DateTime.parse(json['checkInAtUtc']),
      memberName: json['memberName'] ?? '',
      planType: json['planType'] ?? '',
      remainingTime: json['remainingTime'],
    );
  }
}
```

---

## API Request Helper

### Generic HTTP Client with Token Management

```dart
class ApiClient {
  static const String baseUrl = 'http://localhost:5000/api';
  final authService = AuthService();
  final storage = const FlutterSecureStorage();

  Future<Map<String, String>> _getHeaders() async {
    final token = await authService.getAccessToken();
    
    return {
      'Content-Type': 'application/json',
      if (token != null) 'Authorization': 'Bearer $token',
    };
  }

  Future<dynamic> get(String endpoint) async {
    try {
      final headers = await _getHeaders();
      final response = await http.get(
        Uri.parse('$baseUrl$endpoint'),
        headers: headers,
      ).timeout(const Duration(seconds: 30));

      return _handleResponse(response);
    } catch (e) {
      print('GET error: $e');
      rethrow;
    }
  }

  Future<dynamic> post(String endpoint, Map<String, dynamic> body) async {
    try {
      final headers = await _getHeaders();
      final response = await http.post(
        Uri.parse('$baseUrl$endpoint'),
        headers: headers,
        body: jsonEncode(body),
      ).timeout(const Duration(seconds: 30));

      return _handleResponse(response);
    } catch (e) {
      print('POST error: $e');
      rethrow;
    }
  }

  Future<dynamic> put(String endpoint, Map<String, dynamic> body) async {
    try {
      final headers = await _getHeaders();
      final response = await http.put(
        Uri.parse('$baseUrl$endpoint'),
        headers: headers,
        body: jsonEncode(body),
      ).timeout(const Duration(seconds: 30));

      return _handleResponse(response);
    } catch (e) {
      print('PUT error: $e');
      rethrow;
    }
  }

  dynamic _handleResponse(http.Response response) {
    if (response.statusCode == 200 || response.statusCode == 201) {
      return jsonDecode(response.body);
    } else if (response.statusCode == 401) {
      // Token expired, refresh and retry
      throw UnauthorizedException('Unauthorized');
    } else if (response.statusCode == 400) {
      final error = jsonDecode(response.body);
      throw BadRequestException(error['error'] ?? 'Bad request');
    } else if (response.statusCode == 404) {
      throw NotFoundException('Not found');
    } else if (response.statusCode == 429) {
      throw RateLimitException('Too many requests');
    } else {
      throw Exception('Server error: ${response.statusCode}');
    }
  }
}

class UnauthorizedException implements Exception {
  final String message;
  UnauthorizedException(this.message);
}

class BadRequestException implements Exception {
  final String message;
  BadRequestException(this.message);
}

class NotFoundException implements Exception {
  final String message;
  NotFoundException(this.message);
}

class RateLimitException implements Exception {
  final String message;
  RateLimitException(this.message);
}
```

---

## Member Services

### Get Member Profile

```dart
class MemberService {
  final apiClient = ApiClient();

  Future<MemberProfile> getCurrentMemberProfile() async {
    try {
      final response = await apiClient.get('/members/me');
      return MemberProfile.fromJson(response);
    } catch (e) {
      print('Error fetching profile: $e');
      rethrow;
    }
  }

  Future<List<AttendanceRecord>> getMyAttendanceHistory({
    int page = 1,
    int pageSize = 20,
  }) async {
    try {
      final response = await apiClient.get(
        '/members/me/attendance?page=$page&pageSize=$pageSize'
      );
      
      final List<dynamic> data = response['data'] ?? [];
      return data.map((e) => AttendanceRecord.fromJson(e)).toList();
    } catch (e) {
      print('Error fetching attendance: $e');
      rethrow;
    }
  }

  Future<CurrentMembership?> getCurrentMembership() async {
    try {
      final response = await apiClient.get('/members/me/membership');
      return CurrentMembership.fromJson(response);
    } catch (e) {
      print('Error fetching membership: $e');
      return null;
    }
  }
}

class MemberProfile {
  final String id;
  final String memberNumber;
  final String firstName;
  final String lastName;
  final String phoneNumber;
  final String email;
  final DateTime joinDate;
  final String status;

  MemberProfile({
    required this.id,
    required this.memberNumber,
    required this.firstName,
    required this.lastName,
    required this.phoneNumber,
    required this.email,
    required this.joinDate,
    required this.status,
  });

  factory MemberProfile.fromJson(Map<String, dynamic> json) {
    return MemberProfile(
      id: json['id'] ?? '',
      memberNumber: json['memberNumber'] ?? '',
      firstName: json['firstName'] ?? '',
      lastName: json['lastName'] ?? '',
      phoneNumber: json['phoneNumber'] ?? '',
      email: json['email'] ?? '',
      joinDate: DateTime.parse(json['joinDate']),
      status: json['status'] ?? '',
    );
  }

  String get fullName => '$firstName $lastName';
}

class AttendanceRecord {
  final DateTime date;
  final DateTime checkInTime;
  final DateTime? checkOutTime;
  final Duration? duration;
  final String entryMethod;

  AttendanceRecord({
    required this.date,
    required this.checkInTime,
    this.checkOutTime,
    this.duration,
    required this.entryMethod,
  });

  factory AttendanceRecord.fromJson(Map<String, dynamic> json) {
    return AttendanceRecord(
      date: DateTime.parse(json['date']),
      checkInTime: DateTime.parse(json['checkInTime']),
      checkOutTime: json['checkOutTime'] != null 
          ? DateTime.parse(json['checkOutTime']) 
          : null,
      duration: json['duration'] != null 
          ? Duration(hours: int.parse(json['duration'].split(':')[0]))
          : null,
      entryMethod: json['entryMethod'] ?? 'qr',
    );
  }
}

class CurrentMembership {
  final String id;
  final String planName;
  final String status;
  final DateTime startDate;
  final DateTime expiryDate;
  final double price;
  final bool isFrozen;
  final DateTime? freezeStartDate;
  final DateTime? freezeEndDate;

  CurrentMembership({
    required this.id,
    required this.planName,
    required this.status,
    required this.startDate,
    required this.expiryDate,
    required this.price,
    required this.isFrozen,
    this.freezeStartDate,
    this.freezeEndDate,
  });

  factory CurrentMembership.fromJson(Map<String, dynamic> json) {
    return CurrentMembership(
      id: json['id'] ?? '',
      planName: json['planName'] ?? '',
      status: json['status'] ?? '',
      startDate: DateTime.parse(json['startDate']),
      expiryDate: DateTime.parse(json['expiryDate']),
      price: (json['price'] as num).toDouble(),
      isFrozen: json['isFrozen'] ?? false,
      freezeStartDate: json['freezeStartDate'] != null 
          ? DateTime.parse(json['freezeStartDate'])
          : null,
      freezeEndDate: json['freezeEndDate'] != null 
          ? DateTime.parse(json['freezeEndDate'])
          : null,
    );
  }

  bool get isExpired => DateTime.now().isAfter(expiryDate);
  int get daysRemaining => expiryDate.difference(DateTime.now()).inDays;
}
```

---

## UI Widgets

### QR Scanner Widget

```dart
import 'package:mobile_scanner/mobile_scanner.dart';

class QrScannerScreen extends StatefulWidget {
  @override
  State<QrScannerScreen> createState() => _QrScannerScreenState();
}

class _QrScannerScreenState extends State<QrScannerScreen> {
  final qrCheckinService = QrCheckinService();
  bool isProcessing = false;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Gym Check-in')),
      body: Stack(
        children: [
          MobileScanner(
            onDetect: (capture) async {
              if (isProcessing) return;
              
              final List<Barcode> barcodes = capture.barcodes;
              for (final barcode in barcodes) {
                if (barcode.rawValue != null) {
                  setState(() => isProcessing = true);
                  
                  try {
                    final response = 
                        await qrCheckinService.processQrCheckin(barcode.rawValue!);
                    
                    if (response != null && response.success) {
                      _showSuccessDialog(response);
                    } else {
                      _showErrorDialog(response?.message ?? 'Check-in failed');
                    }
                  } catch (e) {
                    _showErrorDialog(e.toString());
                  } finally {
                    setState(() => isProcessing = false);
                  }
                }
              }
            },
          ),
          if (isProcessing)
            Center(
              child: Container(
                color: Colors.black54,
                child: const CircularProgressIndicator(),
              ),
            ),
        ],
      ),
    );
  }

  void _showSuccessDialog(CheckinResponse response) {
    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('✓ Check-in Successful'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Welcome, ${response.memberName}!'),
            Text('Plan: ${response.planType}'),
            Text('Time: ${response.checkInTime.toString()}'),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('OK'),
          ),
        ],
      ),
    );
  }

  void _showErrorDialog(String error) {
    showDialog(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('✗ Check-in Failed'),
        content: Text(error),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('OK'),
          ),
        ],
      ),
    );
  }
}
```

### Member Profile Widget

```dart
class MemberProfileScreen extends StatefulWidget {
  @override
  State<MemberProfileScreen> createState() => _MemberProfileScreenState();
}

class _MemberProfileScreenState extends State<MemberProfileScreen> {
  final memberService = MemberService();
  late Future<MemberProfile> profileFuture;
  late Future<CurrentMembership?> membershipFuture;

  @override
  void initState() {
    super.initState();
    profileFuture = memberService.getCurrentMemberProfile();
    membershipFuture = memberService.getCurrentMembership();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('My Profile')),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(16),
        child: Column(
          children: [
            FutureBuilder<MemberProfile>(
              future: profileFuture,
              builder: (context, snapshot) {
                if (snapshot.connectionState == ConnectionState.waiting) {
                  return const CircularProgressIndicator();
                }
                if (snapshot.hasError) {
                  return Text('Error: ${snapshot.error}');
                }
                
                final profile = snapshot.data;
                return Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      children: [
                        Text(profile?.fullName ?? '',
                            style: const TextStyle(fontSize: 24, fontWeight: FontWeight.bold)),
                        Text('Member #${profile?.memberNumber}'),
                        Text(profile?.email ?? ''),
                        const SizedBox(height: 8),
                        Chip(label: Text(profile?.status ?? 'unknown')),
                      ],
                    ),
                  ),
                );
              },
            ),
            const SizedBox(height: 24),
            FutureBuilder<CurrentMembership?>(
              future: membershipFuture,
              builder: (context, snapshot) {
                if (snapshot.connectionState == ConnectionState.waiting) {
                  return const CircularProgressIndicator();
                }
                if (snapshot.hasError || snapshot.data == null) {
                  return const Text('No active membership');
                }
                
                final membership = snapshot.data!;
                return Card(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const Text('Current Membership',
                            style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
                        const SizedBox(height: 12),
                        Row(
                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                          children: [
                            const Text('Plan:'),
                            Text(membership.planName, fontWeight: FontWeight.bold),
                          ],
                        ),
                        Row(
                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                          children: [
                            const Text('Status:'),
                            Text(membership.status),
                          ],
                        ),
                        Row(
                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                          children: [
                            const Text('Days Remaining:'),
                            Text('${membership.daysRemaining} days',
                                style: const TextStyle(
                                    fontSize: 18, fontWeight: FontWeight.bold,
                                    color: Colors.green)),
                          ],
                        ),
                        Row(
                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                          children: [
                            const Text('Expires:'),
                            Text(membership.expiryDate.toString().split(' ')[0]),
                          ],
                        ),
                      ],
                    ),
                  ),
                );
              },
            ),
          ],
        ),
      ),
    );
  }
}
```

---

## Error Handling Best Practices

```dart
class ErrorHandler {
  static String getErrorMessage(dynamic error) {
    if (error is UnauthorizedException) {
      return 'Your session has expired. Please log in again.';
    } else if (error is BadRequestException) {
      return 'Invalid request: ${error.message}';
    } else if (error is NotFoundException) {
      return 'Requested item not found.';
    } else if (error is RateLimitException) {
      return 'Too many requests. Please wait and try again.';
    } else if (error is SocketException) {
      return 'No internet connection. Please check your network.';
    } else if (error is TimeoutException) {
      return 'Request timed out. Please try again.';
    } else {
      return 'An error occurred. Please try again later.';
    }
  }

  static void showErrorSnackBar(BuildContext context, dynamic error) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(getErrorMessage(error)),
        backgroundColor: Colors.red,
        duration: const Duration(seconds: 4),
      ),
    );
  }
}
```

---

## Environment Configuration

```dart
class ApiConfig {
  static const String devBaseUrl = 'http://localhost:5000';
  static const String stagingBaseUrl = 'https://staging-api.gymflowpro.test';
  static const String prodBaseUrl = 'https://api.gymflowpro.test';

  static String getBaseUrl() {
    const String env = String.fromEnvironment('ENV', defaultValue: 'dev');
    
    switch (env) {
      case 'staging':
        return stagingBaseUrl;
      case 'prod':
        return prodBaseUrl;
      default:
        return devBaseUrl;
    }
  }
}
```

---

## Resources

- **API Documentation**: See `API_DOCUMENTATION_FRONTEND.md`
- **QR Code Library**: `qr_flutter` package for generating gym QR codes
- **Scanner Library**: `mobile_scanner` for QR code scanning
- **State Management**: Use `Provider` for managing auth state
- **Database**: `sqflite` for local caching of member data

