# LanDesk Troubleshooting Guide

## Application Won't Start

### Issue: Application doesn't launch or crashes immediately

**Possible Causes:**
1. Ports 54987 (UDP) or 54988 (TCP) are already in use
2. Missing .NET runtime (if not using self-contained build)
3. Firewall blocking the application
4. Antivirus blocking the application

**Solutions:**

1. **Check if ports are in use:**
   ```powershell
   netstat -ano | findstr "54987"
   netstat -ano | findstr "54988"
   ```
   If ports are in use, close the application using them or change the ports in code.

2. **Run as Administrator:**
   - Right-click the executable
   - Select "Run as administrator"
   - This may be needed for port binding

3. **Check Windows Firewall:**
   - Open Windows Firewall settings
   - Ensure LanDesk is allowed
   - Or run `setup-firewall.ps1` as Administrator

4. **Check Antivirus:**
   - Add LanDesk.exe to antivirus exclusions
   - Some antivirus software blocks unsigned executables

5. **Check Event Viewer:**
   - Open Event Viewer (Windows Logs > Application)
   - Look for errors related to LanDesk

## Connection Issues

### Issue: Cannot connect to remote device

**Possible Causes:**
1. Devices not on same network
2. Firewall blocking connections
3. Device is offline
4. Port conflicts

**Solutions:**

1. **Verify Network:**
   - Ensure both devices are on the same LAN
   - Ping the remote device: `ping <IP_ADDRESS>`

2. **Check Firewall:**
   - Run `setup-firewall.ps1` on both devices
   - Or manually allow ports 54987 (UDP) and 54988 (TCP)

3. **Verify Discovery:**
   - Click "Start Discovery" on both devices
   - Wait a few seconds for devices to appear
   - Check if devices show as "Online"

## Screen Sharing Issues

### Issue: Screen not displaying or black screen

**Possible Causes:**
1. Screen capture permissions
2. Graphics driver issues
3. High CPU usage
4. Network bandwidth

**Solutions:**

1. **Check Permissions:**
   - Ensure application has screen capture permissions
   - On Windows 11, check Privacy settings

2. **Reduce Quality:**
   - Edit `ScreenCaptureService` to lower quality (default: 70)
   - Lower frame rate (default: 10 FPS)

3. **Check Network:**
   - Ensure stable LAN connection
   - Check network bandwidth

## Remote Control Issues

### Issue: Mouse/keyboard input not working

**Possible Causes:**
1. Window not focused
2. Input injection permissions
3. Coordinate mapping issues

**Solutions:**

1. **Focus Window:**
   - Click on the remote desktop window
   - Ensure window has focus for keyboard input

2. **Check Permissions:**
   - Some systems require elevated permissions for input injection
   - Try running as Administrator

3. **Verify Connection:**
   - Check status bar shows "Receiving screen..."
   - Ensure connection is active

## Performance Issues

### Issue: High CPU or memory usage

**Solutions:**

1. **Reduce Frame Rate:**
   - Lower frame rate in `ScreenCaptureService.StartCapture()`
   - Default is 10 FPS, try 5 FPS

2. **Reduce Quality:**
   - Lower JPEG quality (default: 70, try 50)
   - Smaller file sizes = less CPU

3. **Close Unused Connections:**
   - Close remote desktop windows when not in use
   - Stop discovery when not needed

## Build Issues

### Issue: Build fails

**Solutions:**

1. **Clean and Rebuild:**
   ```bash
   dotnet clean
   dotnet restore
   dotnet build
   ```

2. **Check .NET SDK:**
   - Ensure .NET 8.0 SDK is installed
   - Run `dotnet --version` to verify

3. **Check Dependencies:**
   - Ensure all NuGet packages are restored
   - Run `dotnet restore`

## Common Error Messages

### "Port already in use"
- Another instance of LanDesk is running
- Another application is using the port
- Solution: Close other instances or change ports

### "Access denied" or "Permission denied"
- Application needs elevated permissions
- Solution: Run as Administrator

### "Connection refused"
- Remote device is not listening
- Firewall is blocking
- Solution: Check remote device is running LanDesk and firewall settings

### "Device not found"
- Device is offline
- Not on same network
- Discovery not started
- Solution: Start discovery, verify network, check device is online

## Getting Help

If issues persist:
1. Check Windows Event Viewer for detailed errors
2. Run application from command line to see error messages
3. Check firewall and antivirus logs
4. Verify network connectivity between devices
